"""What a Windows program carries (compiler and SDK plan, stage K6): its subsystem, sections, imports,
resources (icon groups and their images, the version block, the manifest, anything else), and what
is appended after its last section. Reads R2025b's mcc builds and JGraph's alike; standard library
only. One key<TAB>value line per fact.

    python pe-resources.py program.exe [another.exe ...]
"""
import struct
import sys

RESOURCE_TYPES = {1: 'CURSOR', 2: 'BITMAP', 3: 'ICON', 4: 'MENU', 5: 'DIALOG', 6: 'STRING', 9: 'ACCELERATOR',
                  10: 'RCDATA', 11: 'MESSAGETABLE', 12: 'GROUP_CURSOR', 14: 'GROUP_ICON', 16: 'VERSION',
                  24: 'MANIFEST'}
SUBSYSTEMS = {2: 'windows-gui', 3: 'windows-console'}


class Pe:
    def __init__(self, data):
        self.data = data
        pe = struct.unpack_from('<I', data, 0x3C)[0]
        if data[pe:pe + 4] != b'PE\0\0':
            raise ValueError('not a PE file')
        self.machine, nsections, _, _, _, optsize, _ = struct.unpack_from('<HHIIIHH', data, pe + 4)
        opt = pe + 24
        magic = struct.unpack_from('<H', data, opt)[0]
        self.pe32plus = magic == 0x20B
        self.subsystem = struct.unpack_from('<H', data, opt + 68)[0]
        dirs = opt + (112 if self.pe32plus else 96)
        ndirs = struct.unpack_from('<I', data, dirs - 4)[0]
        self.dirs = [struct.unpack_from('<II', data, dirs + 8 * k) for k in range(ndirs)]
        self.sections = []
        sec = opt + optsize
        for k in range(nsections):
            name, vsize, va, rawsize, rawptr = struct.unpack_from('<8sIIII', data, sec + 40 * k)
            self.sections.append((name.rstrip(b'\0').decode('ascii', 'replace'), va, vsize, rawptr, rawsize))

    def offset(self, rva):
        for _, va, vsize, rawptr, rawsize in self.sections:
            if va <= rva < va + max(vsize, rawsize):
                return rawptr + rva - va
        raise ValueError('rva %x outside every section' % rva)

    def image_end(self):
        end = max(rawptr + rawsize for _, _, _, rawptr, rawsize in self.sections)
        if len(self.dirs) > 4 and self.dirs[4][0]:
            # The certificate table's "rva" is a file offset.
            end = max(end, self.dirs[4][0] + self.dirs[4][1])
        return end

    def cstring(self, off):
        return self.data[off:self.data.index(b'\0', off)].decode('ascii', 'replace')

    def imports(self):
        names = []
        for index, label in ((1, ''), (13, ' (delay)')):
            if len(self.dirs) <= index or not self.dirs[index][0]:
                continue
            off = self.offset(self.dirs[index][0])
            step = 20 if index == 1 else 32
            name_at = 12 if index == 1 else 4
            while True:
                entry = self.data[off:off + step]
                if entry == b'\0' * step:
                    break
                names.append(self.cstring(self.offset(struct.unpack_from('<I', entry, name_at)[0])) + label)
                off += step
        return names

    def resources(self):
        if len(self.dirs) <= 2 or not self.dirs[2][0]:
            return []
        base = self.offset(self.dirs[2][0])
        found = []

        def walk(off, path):
            named, ids = struct.unpack_from('<HH', self.data, off + 12)
            for k in range(named + ids):
                name, target = struct.unpack_from('<II', self.data, off + 16 + 8 * k)
                if name & 0x80000000:
                    at = base + (name & 0x7FFFFFFF)
                    length = struct.unpack_from('<H', self.data, at)[0]
                    key = self.data[at + 2:at + 2 + 2 * length].decode('utf-16-le')
                else:
                    key = name
                if target & 0x80000000:
                    walk(base + (target & 0x7FFFFFFF), path + [key])
                else:
                    rva, size, codepage, _ = struct.unpack_from('<IIII', self.data, base + target)
                    found.append((path + [key], self.data[self.offset(rva):self.offset(rva) + size], codepage))

        walk(base, [])
        return found


def type_name(key):
    return RESOURCE_TYPES.get(key, str(key)) if isinstance(key, int) else '"%s"' % key


def describe_icon_image(blob):
    if blob[:8] == b'\x89PNG\r\n\x1a\n':
        w, h = struct.unpack_from('>II', blob, 16)
        return 'png %dx%d' % (w, h)
    size, w, h, planes, bits = struct.unpack_from('<IiiHH', blob, 0)
    return 'dib %dx%d %dbpp' % (w, h // 2, bits)


def version_strings(blob):
    """The fixed file info and every string of a VS_VERSIONINFO block."""
    out = []

    def node(off, end, depth):
        while off < end:
            length, value_length, kind = struct.unpack_from('<HHH', blob, off)
            if length == 0:
                break
            key_at = off + 6
            key_end = key_at
            while blob[key_end:key_end + 2] != b'\0\0':
                key_end += 2
            key = blob[key_at:key_end].decode('utf-16-le')
            value_at = (key_end + 2 + 3) & ~3
            if depth == 0:
                if value_length:
                    sig, _, fvms, fvls, pvms, pvls, mask, flags, os_, ftype = struct.unpack_from('<IIIIIIIIII', blob, value_at)
                    out.append(('fixed.FileVersion', '%d.%d.%d.%d' % (fvms >> 16, fvms & 0xFFFF, fvls >> 16, fvls & 0xFFFF)))
                    out.append(('fixed.ProductVersion', '%d.%d.%d.%d' % (pvms >> 16, pvms & 0xFFFF, pvls >> 16, pvls & 0xFFFF)))
                    out.append(('fixed.FileFlags', '0x%x' % flags))
                    out.append(('fixed.FileOS', '0x%x' % os_))
                    out.append(('fixed.FileType', '0x%x' % ftype))
                children = (value_at + value_length + 3) & ~3
                node(children, off + length, 1)
            elif key == 'StringFileInfo':
                node(value_at, off + length, 2)
            elif key == 'VarFileInfo':
                node(value_at, off + length, 4)
            elif depth == 2:
                out.append(('table', key))
                node(value_at, off + length, 3)
            elif depth == 3:
                text = blob[value_at:value_at + 2 * value_length].decode('utf-16-le').rstrip('\0') if value_length else ''
                out.append(('string.' + key, text))
            elif depth == 4:
                langs = [struct.unpack_from('<HH', blob, value_at + 4 * k) for k in range(value_length // 4)]
                out.append(('var.' + key, ' '.join('%04x%04x' % l for l in langs)))
            off = (off + length + 3) & ~3

    node(0, len(blob), 0)
    return out


def report(path):
    data = open(path, 'rb').read()
    pe = Pe(data)
    print('file\t%s' % path.replace('\\', '/').split('/')[-1])
    print('bytes\t%d' % len(data))
    print('machine\t0x%x' % pe.machine)
    print('subsystem\t%s' % SUBSYSTEMS.get(pe.subsystem, pe.subsystem))
    print('sections\t%s' % ' '.join(s[0] for s in pe.sections))
    end = pe.image_end()
    print('image.bytes\t%d' % end)
    tail = data[end:]
    print('overlay.bytes\t%d' % len(tail))
    if tail:
        print('overlay.starts\t%s' % ('PK zip' if tail[:4] == b'PK\x03\x04' else tail[:8].hex(' ')))
    print('certificate\t%s' % ('yes' if len(pe.dirs) > 4 and pe.dirs[4][0] else 'no'))
    for name in pe.imports():
        print('import\t%s' % name)
    resources = pe.resources()
    icons = {}
    for path_, blob, _ in resources:
        if path_[0] == 3:
            icons[(path_[1], path_[2] if len(path_) > 2 else 0)] = blob
    for path_, blob, codepage in resources:
        kind = type_name(path_[0])
        print('resource\t%s %s lang %s: %d bytes' % (kind, path_[1], path_[2] if len(path_) > 2 else '-', len(blob)))
        if path_[0] == 14:
            _, _, count = struct.unpack_from('<HHH', blob, 0)
            for k in range(count):
                w, h, colors, _, planes, bits, size, ident = struct.unpack_from('<BBBBHHIH', blob, 6 + 14 * k)
                lang = path_[2] if len(path_) > 2 else 0
                image = describe_icon_image(icons[(ident, lang)]) if (ident, lang) in icons else 'missing'
                print('  icon\t%dx%d colors %d planes %d bits %d bytes %d id %d: %s' % (w, h, colors, planes, bits, size, ident, image))
        elif path_[0] == 16:
            for key, value in version_strings(blob):
                print('  %s\t%s' % (key, value))
        elif path_[0] == 24:
            text = blob.decode('utf-8', 'replace').lstrip('﻿')
            for line in text.splitlines():
                if line.strip():
                    print('  manifest\t%s' % line.strip())
        elif path_[0] == 10 or isinstance(path_[0], str):
            print('  starts\t%s' % (blob[:16].hex(' ')))


sys.stdout.reconfigure(encoding='utf-8', newline='\n')
for p in sys.argv[1:]:
    try:
        report(p)
    except Exception as e:  # noqa: BLE001 - a probe reports and goes on
        print('file\t%s\nERR\t%s' % (p, e))
