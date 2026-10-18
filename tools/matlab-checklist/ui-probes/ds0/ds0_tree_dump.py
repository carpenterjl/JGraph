"""DS0: lists the subsystem objects of an uncompressed version 7 MAT-file - each object's class and
the property names it saved - using the FileWrapper__ table layout JGraph's MatMcos reads (U11,
ADR 0210). Usage: python ds0_tree_dump.py file.mat [file.mat ...]"""
import struct
import sys
from collections import Counter


def tag(buf, at):
    t, n = struct.unpack_from('<II', buf, at)
    if t >> 16:  # small data element
        return t & 0xFFFF, t >> 16, at + 4, at + 8
    return t, n, at + 8, at + 8 + ((n + 7) // 8) * 8


def matrix(buf, at):
    """Reads one miMATRIX element at at; returns (value, next offset)."""
    t, n, data, nxt = tag(buf, at)
    assert t == 14, f'not a matrix at {at}: type {t}'
    end = data + n
    p = data
    ft, fn, fd, p = tag(buf, p)
    flags = struct.unpack_from('<I', buf, fd)[0]
    cls = flags & 0xFF
    if cls == 17:  # opaque: name, type system, class name, then the content
        for _ in range(3):
            _, _, _, p = tag(buf, p)
        content, _ = matrix(buf, p)
        return ('opaque', content), nxt
    dt, dn, dd, p = tag(buf, p)
    dims = struct.unpack_from(f'<{dn // 4}i', buf, dd)
    count = 1
    for d in dims:
        count *= d
    _, _, _, p = tag(buf, p)  # array name
    if cls == 1:  # cell
        items = []
        for _ in range(count):
            v, p = matrix(buf, p)
            items.append(v)
        return ('cell', items), nxt
    if cls == 2:  # struct
        lt, ln, ld, p = tag(buf, p)
        width = struct.unpack_from('<i', buf, ld)[0]
        nt, nn, nd, p = tag(buf, p)
        names = [buf[nd + i * width: nd + (i + 1) * width].split(b'\0')[0].decode() for i in range(nn // width)]
        fields = {}
        for _ in range(count):
            for nm in names:
                v, p = matrix(buf, p)
                fields.setdefault(nm, []).append(v)
        return ('struct', fields), nxt
    rt, rn, rd, p = tag(buf, p)
    return ('numeric', bytes(buf[rd:rd + rn]), rt, dims), nxt


def table(fw):
    """Decodes the FileWrapper__ cell's byte table as MatMcos does."""
    raw = fw[1][0][1]
    u32 = lambda a: struct.unpack_from('<I', raw, a)[0]
    count = u32(4)
    offs = [u32(8 + 4 * i) for i in range(8)]
    names, at = [''], 40
    while len(names) <= count:
        e = raw.index(b'\0', at)
        names.append(raw[at:e].decode())
        at = e + 1

    def records(a, b, w):
        return [[u32(x + 4 * i) for i in range(w)] for x in range(a, b, 4 * w)]

    def plists(a, b):
        out, x = [], a
        while x + 4 <= b:
            c = u32(x); x += 4
            out.append([(u32(x + 12 * i), u32(x + 12 * i + 4), u32(x + 12 * i + 8)) for i in range(c)])
            x += 12 * c
            if (x - a) % 8:
                x += 4
        return out

    classes = records(offs[0], offs[1], 4)
    saved = plists(offs[1], offs[2])
    objects = records(offs[2], offs[3], 6)
    plain = plists(offs[3], offs[4])
    return names, classes, saved, objects, plain


def dump(path):
    buf = open(path, 'rb').read()
    sub = struct.unpack_from('<Q', buf, 116)[0]
    inner, _ = matrix(buf, sub)
    blob = inner[1]
    # the subsystem's bytes are a MAT stream without the 128-byte header: 8 bytes, then a struct
    st, _ = matrix(blob, 8)
    fw = st[1]['MCOS'][0][1]
    names, classes, saved, objects, plain = table(fw)
    cname = lambda c: (names[classes[c][0]] + '.' if classes[c][0] else '') + names[classes[c][1]]
    print(f'== {path}: {len(objects) - 1} objects, {len(names) - 1} names, {len(fw[1]) - 2} stored values')
    per_class = Counter()
    for i, o in enumerate(objects[1:], 1):
        cls = cname(o[0])
        lst = plain[o[4]] if 0 < o[4] < len(plain) else saved[o[3]] if 0 < o[3] < len(saved) else []
        per_class[cls] += 1
        kinds = Counter(k for _, k, _ in lst)
        props = ','.join(names[n] for n, _, _ in lst)
        via = 'plain' if 0 < o[4] < len(plain) else 'saveobj' if 0 < o[3] < len(saved) else 'none'
        print(f'{i:4d} {cls} [{via}, {len(lst)} props, kinds {dict(kinds)}]: {props[:600]}')
    print('per class:', dict(per_class))


for f in sys.argv[1:]:
    dump(f)
