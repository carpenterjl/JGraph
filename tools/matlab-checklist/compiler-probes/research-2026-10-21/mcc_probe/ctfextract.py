"""Extract the plain-text (XML/JSON/txt) entries of a CTF (OPC zip) into a folder."""
import sys
import zipfile
import os

src, dest = sys.argv[1], sys.argv[2]
os.makedirs(dest, exist_ok=True)
z = zipfile.ZipFile(src)
for zi in z.infolist():
    name = zi.filename
    if name.endswith(('.xml', '.rels', '.json', '.txt', '.sigs', '.prf')) or name.startswith('[Content'):
        out = os.path.join(dest, name.replace('/', os.sep))
        os.makedirs(os.path.dirname(out), exist_ok=True)
        with open(out, 'wb') as f:
            f.write(z.read(zi))
# also record which code entries look encrypted
for zi in z.infolist():
    if zi.filename.endswith(('.m', '.mat', '.p', '.mlx')):
        head = z.read(zi)[:24]
        print('%-60s %s' % (zi.filename, head))
