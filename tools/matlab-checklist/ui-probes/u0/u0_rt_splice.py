"""U0 save-back round trip, part B: rewrite an .mlapp the way U7b would.

- document.xml gets the edited text inside the same CDATA run;
- appModel.mat keeps every element byte for byte except `code`, which is replaced by the
  uncompressed element MATLAB wrote with save -v6 (JGraph's MatFileWriter writes the same form);
- every other OPC part is copied unchanged, in the same order and with the same compression.
"""
import io, os, re, struct, sys, zipfile, zlib

rt = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'rt')
src = os.path.join(rt, 'DataExport.mlapp')
os.makedirs(os.path.join(rt, 'out'), exist_ok=True)
dst = os.path.join(rt, 'out', 'DataExport.mlapp')


def elements(mat: bytes):
    """Yield (offset, length, name, raw) for each top-level element of a level-5 MAT-file."""
    pos = 128
    while pos + 8 <= len(mat):
        typ, nbytes = struct.unpack_from('<II', mat, pos)
        if typ == 15:  # miCOMPRESSED: no padding
            total = 8 + nbytes
            body = zlib.decompress(mat[pos + 8:pos + 8 + nbytes])
        else:
            total = 8 + nbytes + ((8 - nbytes % 8) % 8)
            body = mat[pos:pos + 8 + nbytes]
        yield pos, total, name_of(body), mat[pos:pos + total]
        pos += total


def name_of(matrix: bytes) -> str:
    # miMATRIX tag (8), array flags element (16), dims element (tag + padded), name element
    p = 8 + 16
    t, n = struct.unpack_from('<II', matrix, p)
    if t >> 16:  # small data element
        p += 8
    else:
        p += 8 + n + ((8 - n % 8) % 8)
    t, n = struct.unpack_from('<II', matrix, p)
    if t >> 16:
        n = t >> 16
        return matrix[p + 4:p + 4 + n].decode('ascii')
    return matrix[p + 8:p + 8 + n].decode('ascii')


with zipfile.ZipFile(src) as z:
    infos = z.infolist()
    parts = {i.filename: z.read(i.filename) for i in infos}

model_name = next(n for n in parts if n.endswith('appModel.mat'))
old = parts[model_name]
new_code = open(os.path.join(rt, 'code_v6.mat'), 'rb').read()
code_el = [raw for _, _, nm, raw in elements(new_code) if nm == 'code']
assert len(code_el) == 1
out = bytearray(old[:128])
names = []
# Header bytes 116..123 hold the subsystem-data offset (MCOS object data). It must follow its element.
subsys_old = struct.unpack_from('<Q', old, 116)[0]
subsys_new = 0
for off, _, nm, raw in elements(old):
    names.append(nm)
    if off == subsys_old:
        subsys_new = len(out)
    out += code_el[0] if nm == 'code' else raw
if subsys_old:
    assert subsys_new, 'subsystem offset did not land on an element boundary'
    struct.pack_into('<Q', out, 116, subsys_new)
print('appModel.mat elements:', names, 'subsystem offset', subsys_old, '->', subsys_new)
parts[model_name] = bytes(out)

doc_name = 'matlab/document.xml'
doc = parts[doc_name].decode('utf-8')
text = open(os.path.join(rt, 'document_text.m'), encoding='utf-8').read()
m = re.search(r'<!\[CDATA\[(.*?)\]\]>', doc, re.S)
assert m and ']]>' not in text
doc = doc[:m.start(1)] + text + doc[m.end(1):]
parts[doc_name] = doc.encode('utf-8')

tmp = dst + '.tmp'
with zipfile.ZipFile(tmp, 'w') as z:
    for i in infos:
        zi = zipfile.ZipInfo(i.filename, date_time=i.date_time)
        zi.compress_type = i.compress_type
        zi.external_attr = i.external_attr
        z.writestr(zi, parts[i.filename])
os.replace(tmp, dst)
print('wrote', dst, os.path.getsize(dst), 'bytes; parts:', [i.filename for i in infos])
