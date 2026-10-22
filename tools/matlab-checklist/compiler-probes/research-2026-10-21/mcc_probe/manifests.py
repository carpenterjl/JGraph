"""Print the key parts of .META/manifest.xml from each CTF-bearing file given."""
import io
import re
import sys
import zipfile
import xml.etree.ElementTree as ET


def ctf_bytes(path):
    data = open(path, 'rb').read()
    if path.lower().endswith('.dll') and b'PK\x03\x04' in data:
        start = data.find(b'PK\x03\x04')
        return data[start:]
    return data


for path in sys.argv[1:]:
    print('=' * 70)
    print(path)
    try:
        z = zipfile.ZipFile(io.BytesIO(ctf_bytes(path)))
    except Exception as e:
        print('  not a zip:', e)
        continue
    names = [i.filename for i in z.infolist()]
    print('  entries:', len(names), ' fsroot code:', [n for n in names if n.startswith('fsroot/') and not n.startswith('fsroot/.') and not n.startswith('fsroot/toolbox')])
    root = ET.fromstring(z.read('.META/manifest.xml'))
    attrs = {k: v for k, v in root.attrib.items() if k not in ('signature',)}
    print('  root attrs:', attrs)
    for f in root.iter('function'):
        ins = [p.get('name') for p in f.findall('./inputs/param')]
        outs = [p.get('name') for p in f.findall('./outputs/param')]
        if f.find('name') is not None:
            print('  function %s(%s) -> [%s]' % (f.find('name').text, ', '.join(ins), ', '.join(outs)))
    for tag in ('runtime-options', 'warning-state', 'application-options', 'java-class-path', 'library-path', 'main-function', 'public-functions'):
        for el in root.iter(tag):
            print('  %s: %s' % (tag, [(c.tag, c.text, dict(c.attrib)) for c in el]))
    paths = [p.get('ref') for p in root.iter('path-item')]
    print('  matlab-path items:', len(paths), ' first 6:', paths[:6])
    # any other top-level config children
    cfg = root.find('configuration')
    if cfg is not None:
        print('  configuration children:', [c.tag for c in cfg])
