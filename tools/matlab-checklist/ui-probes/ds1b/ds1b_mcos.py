"""Raw level-5 MAT reader/writer that keeps every element exactly, plus the MCOS subsystem table as a model.
For the DS1b go/no-go experiment: rewrite a MATLAB-saved component tree with property entries removed."""
import struct

MI_INT8, MI_UINT8, MI_INT32, MI_UINT32, MI_MATRIX = 1, 2, 5, 6, 14


def tag(buf, at):
    t, n = struct.unpack_from('<II', buf, at)
    if t >> 16:
        return t & 0xFFFF, t >> 16, at + 4, at + 8
    return t, n, at + 8, at + 8 + ((n + 7) // 8) * 8


def sub_element(buf, at):
    t, n, d, nxt = tag(buf, at)
    return (t, bytes(buf[d:d + n])), nxt


class Node:
    """A miMATRIX element: kind 'opaque', 'cell', 'struct' or 'plain'."""

    def __init__(self, **kw):
        self.__dict__.update(kw)


def read_matrix(buf, at):
    t, n, d, nxt = tag(buf, at)
    assert t == MI_MATRIX, (t, at)
    if n == 0:
        return Node(kind='empty'), nxt
    end = d + n
    flags, p = sub_element(buf, d)
    cls = flags[1][0]
    if cls == 17:
        name, p = sub_element(buf, p)
        ts, p = sub_element(buf, p)
        cn, p = sub_element(buf, p)
        content, p = read_matrix(buf, p)
        return Node(kind='opaque', flags=flags, name=name, ts=ts, cn=cn, content=content), nxt
    dims, p = sub_element(buf, p)
    name, p = sub_element(buf, p)
    count = 1
    for x in struct.unpack(f'<{len(dims[1]) // 4}i', dims[1]):
        count *= x
    if cls == 1:
        items = []
        for _ in range(count):
            v, p = read_matrix(buf, p)
            items.append(v)
        return Node(kind='cell', flags=flags, dims=dims, name=name, items=items), nxt
    if cls == 2:
        width, p = sub_element(buf, p)
        names, p = sub_element(buf, p)
        w = struct.unpack('<i', width[1])[0]
        nf = len(names[1]) // w if w else 0
        items = []
        for _ in range(count * nf):
            v, p = read_matrix(buf, p)
            items.append(v)
        return Node(kind='struct', flags=flags, dims=dims, name=name, width=width, names=names, items=items), nxt
    rest = []
    while p < end:
        e, p = sub_element(buf, p)
        rest.append(e)
    return Node(kind='plain', flags=flags, dims=dims, name=name, rest=rest), nxt


def emit_sub(t, data):
    if 0 < len(data) <= 4:
        return struct.pack('<HH', t, len(data)) + data + b'\0' * (4 - len(data))
    pad = (-len(data)) % 8
    return struct.pack('<II', t, len(data)) + data + b'\0' * pad


def emit_matrix(node):
    if node.kind == 'empty':
        return struct.pack('<II', MI_MATRIX, 0)
    body = emit_sub(*node.flags)
    if node.kind == 'opaque':
        body += emit_sub(*node.name) + emit_sub(*node.ts) + emit_sub(*node.cn) + emit_matrix(node.content)
    else:
        body += emit_sub(*node.dims) + emit_sub(*node.name)
        if node.kind == 'cell':
            body += b''.join(emit_matrix(x) for x in node.items)
        elif node.kind == 'struct':
            body += emit_sub(*node.width) + emit_sub(*node.names) + b''.join(emit_matrix(x) for x in node.items)
        else:
            body += b''.join(emit_sub(*e) for e in node.rest)
    return struct.pack('<II', MI_MATRIX, len(body)) + body


def plain_bytes(node):
    """The data bytes of a uint8 plain node."""
    return node.rest[0][1]


def uint8_node(data, name=b'', row=False):
    return Node(kind='plain', flags=(MI_UINT32, struct.pack('<II', 9, 0)),
                dims=(MI_INT32, struct.pack('<ii', *((1, len(data)) if row else (len(data), 1)))), name=(MI_INT8, name), rest=[(MI_UINT8, data)])


class Table:
    """The FileWrapper__ byte table as a model."""

    def __init__(self, raw):
        u32 = lambda a: struct.unpack_from('<I', raw, a)[0]
        self.version = u32(0)
        count = u32(4)
        offs = [u32(8 + 4 * i) for i in range(8)]
        names, at = [''], 40
        while len(names) <= count:
            e = raw.index(b'\0', at)
            names.append(raw[at:e].decode())
            at = e + 1
        self.names = names
        self.classes = [[u32(x + 4 * i) for i in range(4)] for x in range(offs[0], offs[1], 16)]
        self.saved = self._lists(raw, offs[1], offs[2])
        self.objects = [[u32(x + 4 * i) for i in range(6)] for x in range(offs[2], offs[3], 24)]
        self.plain = self._lists(raw, offs[3], offs[4])
        self.dyn = self._idlists(raw, offs[4], offs[5])
        self.seg6 = raw[offs[5]:offs[6]]
        self.seg7 = raw[offs[6]:offs[7]]
        self.tail = raw[offs[7]:]

    @staticmethod
    def _lists(raw, a, b):
        u32 = lambda x: struct.unpack_from('<I', raw, x)[0]
        out, x = [], a
        while x + 4 <= b:
            c = u32(x)
            x += 4
            out.append([[u32(x + 12 * i), u32(x + 12 * i + 4), u32(x + 12 * i + 8)] for i in range(c)])
            x += 12 * c
            if (x - a) % 8:
                x += 4
        return out

    @staticmethod
    def _idlists(raw, a, b):
        u32 = lambda x: struct.unpack_from('<I', raw, x)[0]
        out, x = [], a
        while x + 4 <= b:
            c = u32(x)
            out.append([u32(x + 4 + 4 * i) for i in range(c)])
            x += 4 + 4 * c
            if (x - a) % 8:
                x += 4
        return out

    def emit(self):
        names = b''.join(n.encode() + b'\0' for n in self.names[1:])
        names += b'\0' * ((-(40 + len(names))) % 8)
        segs = []
        segs.append(b''.join(struct.pack('<4I', *c) for c in self.classes))
        segs.append(self._emit_lists(self.saved))
        segs.append(b''.join(struct.pack('<6I', *o) for o in self.objects))
        segs.append(self._emit_lists(self.plain))
        seg5 = b''
        for ids in self.dyn:
            e = struct.pack(f'<{1 + len(ids)}I', len(ids), *ids)
            e += b'\0' * ((-len(e)) % 8)
            seg5 += e
        segs.append(seg5)
        segs.append(self.seg6)
        segs.append(self.seg7)
        offs, at = [], 40 + len(names)
        for s in segs:
            offs.append(at)
            at += len(s)
        offs.append(at)
        head = struct.pack('<II', self.version, len(self.names) - 1) + struct.pack('<8I', *offs)
        return head + names + b''.join(segs) + self.tail

    @staticmethod
    def _emit_lists(lists):
        out = b''
        for lst in lists:
            e = struct.pack('<I', len(lst)) + b''.join(struct.pack('<3I', *t) for t in lst)
            e += b'\0' * ((-len(e)) % 8)
            out += e
        return out

    def name_index(self, name):
        if name not in self.names:
            self.names.append(name)
        return self.names.index(name)

    def class_name(self, cid):
        p, n = self.classes[cid][0], self.classes[cid][1]
        return (self.names[p] + '.' if p else '') + self.names[n]


class MatFile:
    def __init__(self, path):
        buf = open(path, 'rb').read()
        self.header = buf[:128]
        sub = struct.unpack_from('<Q', buf, 116)[0]
        self.variables = []
        at = 128
        while at < len(buf):
            if at == sub:
                outer, at = read_matrix(buf, at)
                blob = plain_bytes(outer)
                self.blob_head = blob[:8]
                st, after = read_matrix(blob, 8)
                self.blob_rest = blob[after:]
                self.sub_struct = st
                fw = st.items[0]
                self.cells = fw.content.items
                self.table = Table(plain_bytes(self.cells[0]))
                continue
            v, at = read_matrix(buf, at)
            self.variables.append(v)

    def write(self, path):
        self.cells[0] = uint8_node(self.table.emit())
        fw = self.sub_struct.items[0].content
        fw.items = self.cells
        fw.dims = (MI_INT32, struct.pack('<ii', len(self.cells), 1))
        blob = self.blob_head + emit_matrix(self.sub_struct) + self.blob_rest
        body = b''.join(emit_matrix(v) for v in self.variables)
        sub_at = 128 + len(body)
        header = self.header[:116] + struct.pack('<Q', sub_at) + self.header[124:]
        open(path, 'wb').write(header + body + emit_matrix(uint8_node(blob, row=True)))

    # helpers for experiments
    def objects_of(self, cls):
        return [i for i, o in enumerate(self.table.objects) if i and self.table.class_name(o[0]) == cls]

    def props(self, oid):
        o = self.table.objects[oid]
        return self.table.plain[o[4]] if o[4] else self.table.saved[o[3]]

    def prop_names(self, oid):
        return [self.table.names[t[0]] for t in self.props(oid)]


def char_node(text):
    data = text.encode('utf-8')
    return Node(kind='plain', flags=(MI_UINT32, struct.pack('<II', 4, 0)),
                dims=(MI_INT32, struct.pack('<ii', 1 if text else 0, len(text))), name=(MI_INT8, b''),
                rest=[(16, data)])


def double_node(values, rows=1):
    data = struct.pack(f'<{len(values)}d', *values)
    return Node(kind='plain', flags=(MI_UINT32, struct.pack('<II', 6, 0)),
                dims=(MI_INT32, struct.pack('<ii', rows, len(values) // rows if values else 0)), name=(MI_INT8, b''),
                rest=[(9, data)])


def add_value(m, node):
    """Stores node as a new value cell (ahead of the three per-class cells); returns its value index."""
    m.cells.insert(len(m.cells) - 3, node)
    return len(m.cells) - 3 - 1 - 2


def set_name(m, oid, name, node, replace=None):
    """Gives object oid the stored name `name` holding node, in place of `replace` when given, else appended."""
    lst = m.props(oid)
    entry = [m.table.name_index(name), 1, add_value(m, node)]
    for i, t in enumerate(lst):
        if replace is not None and m.table.names[t[0]] == replace:
            lst[i] = entry
            return
    lst.append(entry)


def show(node, depth=0):
    """A short text for a stored value."""
    k = node.kind
    if k == 'empty':
        return '[]'
    if k == 'plain':
        cls = struct.unpack_from('<I', node.flags[1])[0] & 0xFF
        dims = struct.unpack(f'<{len(node.dims[1]) // 4}i', node.dims[1])
        data = node.rest[0][1] if node.rest else b''
        t = node.rest[0][0] if node.rest else 0
        if cls == 4:
            return repr(data.decode('utf-8', 'replace') if t == 16 else data.decode('utf-16-le', 'replace'))
        fmt = {9: 'd', 7: 'f', 1: 'b', 2: 'B', 3: 'h', 4: 'H', 5: 'i', 6: 'I', 12: 'q', 13: 'Q'}.get(t)
        if fmt:
            vals = struct.unpack(f'<{len(data) // struct.calcsize(fmt)}{fmt}', data)
            if t == 6 and len(vals) > 2 and vals[0] == 0xDD000000:
                return f'ref{list(vals[2 + vals[1]:-1])}'
            txt = ' '.join(f'{v:g}' if isinstance(v, float) else str(v) for v in vals[:8])
            return f'c{cls}{list(dims)}[{txt}{" ..." if len(vals) > 8 else ""}]'
        return f'c{cls}{list(dims)}'
    if k == 'cell':
        return '{' + ', '.join(show(x, depth + 1) for x in node.items[:6]) + (' ...' if len(node.items) > 6 else '') + '}'
    if k == 'struct':
        w = struct.unpack('<i', node.width[1])[0]
        names = [node.names[1][i:i + w].split(b'\0')[0].decode() for i in range(0, len(node.names[1]), w)] if w else []
        if not node.items:
            return 'struct(' + ','.join(names) + ')'
        return 'struct{' + ', '.join(f'{n}:{show(v, depth + 1)}' for n, v in zip(names, node.items[:len(names)])) + '}'
    if k == 'opaque':
        return f'opaque({node.cn[1].decode()}: {show(node.content, depth + 1)})'
    return k


def describe(m, oid):
    """Each stored name of object oid with its value as text."""
    out = []
    for n, kind, v in m.props(oid):
        name = m.table.names[n]
        if kind == 0:
            out.append((name, f'word {m.table.names[v]!r}'))
        elif kind == 1:
            out.append((name, show(m.cells[v + 2])))
        else:
            out.append((name, f'literal {v}'))
    return out


def set_word(m, oid, name, word):
    """Gives object oid the stored name `name` holding the word `word` (kind 0), replacing one there."""
    lst = m.props(oid)
    entry = [m.table.name_index(name), 0, m.table.name_index(word)]
    for i, t in enumerate(lst):
        if m.table.names[t[0]] == name:
            lst[i] = entry
            return
    lst.append(entry)


def drop(m, oid, name):
    lst = m.props(oid)
    lst[:] = [t for t in lst if m.table.names[t[0]] != name]


def cell_node(items, rows=1):
    return Node(kind='cell', flags=(MI_UINT32, struct.pack('<II', 1, 0)),
                dims=(MI_INT32, struct.pack('<ii', rows if items else 0, len(items) // rows if items else 0)),
                name=(MI_INT8, b''), items=items)


def logical_node(values):
    data = bytes(1 if v else 0 for v in values)
    return Node(kind='plain', flags=(MI_UINT32, struct.pack('<II', 9 | 0x200, 0)),
                dims=(MI_INT32, struct.pack('<ii', 1, len(values))), name=(MI_INT8, b''), rest=[(MI_UINT8, data)])


def empty_defaults(m):
    """Empties the per-class default values (the last cell), as a writer that saves no defaults would."""
    import copy
    out = []
    for x in m.cells[-1].items:
        n = copy.copy(x)
        n.items = []
        n.names = (n.names[0], b'')
        out.append(n)
    m.cells[-1].items = out


def value_node(v):
    """A node for a Python value: str -> char, list of str -> cell row, bool list -> logical, numbers -> double row."""
    if isinstance(v, str):
        return char_node(v)
    if isinstance(v, list) and v and all(isinstance(x, str) for x in v):
        return cell_node([char_node(x) for x in v])
    if isinstance(v, (int, float)):
        return double_node([float(v)])
    return double_node([float(x) for x in v])


def blank(m, oid, keep=('matlab.ui.container.Container.SerializableChildren',)):
    lst = m.props(oid)
    lst[:] = [t for t in lst if m.table.names[t[0]] in keep]
