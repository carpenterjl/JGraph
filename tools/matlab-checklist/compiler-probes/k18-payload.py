"""What an installer carries (compiler and SDK plan, stage K18): for R2025b's setup.exe with its
payload embedded as resources (MW_COMPILER_EZDEPLOY_*), the bundle's files and its
ApplicationInstallerManifest.xml, the install agent URL, the splash and the icon; for a .zip output,
the entries and the same of the program inside. Standard library only; pe-resources.py's reader.
One key<TAB>value line per fact, a text's lines indented under its key.

    python k18-payload.py <label> installer.exe|installer.zip [...]
"""
import importlib.util
import io
import os
import struct
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location('pe_resources', os.path.join(HERE, 'pe-resources.py'))
# pe-resources.py reports the programs named on its command line when imported, so it is loaded with none.
saved = sys.argv
sys.argv = [saved[0]]
pe_resources = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pe_resources)
sys.argv = saved


def lines(label, text):
    for line in text.replace('\r\n', '\n').split('\n'):
        if line.strip():
            print('  |' + line.rstrip())


def image_kind(blob):
    if blob[:8] == b'\x89PNG\r\n\x1a\n':
        w, h = struct.unpack_from('>II', blob, 16)
        return 'png %dx%d' % (w, h)
    if blob[:2] == b'BM':
        w, h = struct.unpack_from('<ii', blob, 18)
        return 'bmp %dx%d' % (w, abs(h))
    if blob[:4] == b'\0\0\x01\0':
        return 'ico %d images' % struct.unpack_from('<H', blob, 4)[0]
    if blob[:3] == b'GIF':
        return 'gif'
    return 'bytes ' + blob[:8].hex(' ')


def zip_report(label, data):
    archive = zipfile.ZipFile(io.BytesIO(data))
    entries = archive.infolist()
    print('%s.entries\t%s' % (label, ' | '.join('%s (%d)' % (e.filename, e.file_size) for e in sorted(entries, key=lambda e: e.filename))))
    for e in entries:
        name = e.filename.rsplit('/', 1)[-1]
        if name.endswith('.xml') or name.endswith('.txt') and e.file_size < 20000:
            print('%s.text.%s' % (label, e.filename))
            lines(label, archive.read(e).decode('utf-8', 'replace'))
    return archive


def exe_report(label, data):
    pe = pe_resources.Pe(data)
    print('%s.bytes\t%d' % (label, len(data)))
    print('%s.subsystem\t%s' % (label, pe_resources.SUBSYSTEMS.get(pe.subsystem, pe.subsystem)))
    end = pe.image_end()
    print('%s.overlay.bytes\t%d' % (label, len(data) - end))
    resources = pe.resources()
    icons = {}
    for path, blob, _ in resources:
        if path[0] == 3:
            icons[(path[1], path[2] if len(path) > 2 else 0)] = blob
    for path, blob, _ in resources:
        kind = pe_resources.type_name(path[0])
        if path[0] == 10 and isinstance(path[1], str):
            name = path[1]
            print('%s.resource.%s\t%d bytes' % (label, name, len(blob)))
            if name.endswith('URL'):
                print('%s.text.%s\t%s' % (label, name, blob.decode('utf-8', 'replace').strip()))
            elif name.endswith('SPLASH'):
                print('%s.image.%s\t%s' % (label, name, image_kind(blob)))
            elif blob[:4] == b'PK\x03\x04':
                zip_report('%s.zip.%s' % (label, name), blob)
            else:
                print('%s.starts.%s\t%s' % (label, name, blob[:16].hex(' ')))
        elif path[0] == 14:
            _, _, count = struct.unpack_from('<HHH', blob, 0)
            images = []
            for k in range(count):
                w, h, colors, _, planes, bits, size, ident = struct.unpack_from('<BBBBHHIH', blob, 6 + 14 * k)
                lang = path[2] if len(path) > 2 else 0
                image = pe_resources.describe_icon_image(icons[(ident, lang)]) if (ident, lang) in icons else 'missing'
                images.append('%dx%d %dbpp %s' % (w, h, bits, image))
            print('%s.icongroup.%s\t%s' % (label, path[1], ' | '.join(images)))
        elif path[0] == 16:
            for key, value in pe_resources.version_strings(blob):
                if key.startswith('string.') or key.startswith('fixed.FileVersion') or key.startswith('fixed.ProductVersion'):
                    print('%s.version.%s\t%s' % (label, key, value))
        else:
            print('%s.resource.%s\t%s lang %s: %d bytes' % (label, kind, path[1], path[2] if len(path) > 2 else '-', len(blob)))


def report(label, path):
    data = open(path, 'rb').read()
    name = os.path.basename(path)
    print('%s.file\t%s' % (label, name))
    if name.lower().endswith('.zip'):
        archive = zip_report(label, data)
        for e in archive.infolist():
            if e.filename.lower().endswith('.exe'):
                exe_report('%s.inner.%s' % (label, e.filename.rsplit('/', 1)[-1]), archive.read(e))
    else:
        exe_report(label, data)


sys.stdout.reconfigure(encoding='utf-8', newline='\n')
args = sys.argv[1:]
if len(args) < 2:
    print(__doc__)
    sys.exit(2)
label = args[0]
for p in args[1:]:
    try:
        report(label, p)
    except Exception as e:  # noqa: BLE001 - a probe reports and goes on
        print('%s.file\t%s\nERR\t%s' % (label, p, e))
