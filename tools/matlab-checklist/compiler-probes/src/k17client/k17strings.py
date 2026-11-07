"""The string heaps of a .NET assembly (compiler and SDK plan, stage K17a): the user strings (#US,
the literals a program's code carries: URLs, registry paths, header names, messages) and the
identifiers (#Strings) of R2025b's Excel client DLL and its ServerConfig.dll, so what the client
says to a server and where it keeps its settings can be read without running it. Standard library
only. One key<TAB>value line per fact, the strings as indented lines under their key.

    python k17strings.py <label> assembly.dll
"""
import struct
import sys


def rva_to_offset(data, sections, rva):
    for va, vsize, raw, rsize in sections:
        if va <= rva < va + max(vsize, rsize):
            return rva - va + raw
    raise ValueError('rva %x outside the sections' % rva)


def heaps(data):
    pe = struct.unpack_from('<I', data, 0x3C)[0]
    if data[pe:pe + 4] != b'PE\0\0':
        raise ValueError('not a PE file')
    nsections, optsize = struct.unpack_from('<H', data, pe + 6)[0], struct.unpack_from('<H', data, pe + 20)[0]
    opt = pe + 24
    pe32plus = struct.unpack_from('<H', data, opt)[0] == 0x20B
    dirs = opt + (112 if pe32plus else 96)
    sections = []
    table = opt + optsize
    for k in range(nsections):
        s = table + 40 * k
        vsize, va, rsize, raw = struct.unpack_from('<IIII', data, s + 8)
        sections.append((va, vsize, raw, rsize))
    cli_rva, cli_size = struct.unpack_from('<II', data, dirs + 14 * 8)
    if cli_rva == 0:
        raise ValueError('no CLI header: not a .NET assembly')
    cli = rva_to_offset(data, sections, cli_rva)
    meta_rva, meta_size = struct.unpack_from('<II', data, cli + 8)
    meta = rva_to_offset(data, sections, meta_rva)
    if data[meta:meta + 4] != b'BSJB':
        raise ValueError('no metadata signature')
    vlen = struct.unpack_from('<I', data, meta + 12)[0]
    p = meta + 16 + vlen
    nstreams = struct.unpack_from('<H', data, p + 2)[0]
    p += 4
    out = {}
    for _ in range(nstreams):
        off, size = struct.unpack_from('<II', data, p)
        p += 8
        end = data.index(b'\0', p)
        name = data[p:end].decode('ascii')
        p = (end + 1 + 3) & ~3
        out[name] = data[meta + off:meta + off + size]
    return out


def user_strings(blob):
    # Each entry: a compressed length, then UTF-16LE characters and one trailing flag byte.
    p = 1
    while p < len(blob):
        b0 = blob[p]
        if b0 & 0x80 == 0:
            n, p = b0, p + 1
        elif b0 & 0xC0 == 0x80:
            n, p = ((b0 & 0x3F) << 8) | blob[p + 1], p + 2
        else:
            n, p = ((b0 & 0x1F) << 24) | (blob[p + 1] << 16) | (blob[p + 2] << 8) | blob[p + 3], p + 4
        if n == 0:
            continue
        chars = blob[p:p + n - 1]
        p += n
        yield chars.decode('utf-16-le', 'replace')


def escape(s):
    return ''.join(c if 32 <= ord(c) < 127 else '\\u%04x' % ord(c) for c in s).replace('\t', '\\t')


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    label, path = argv[1], argv[2]
    data = open(path, 'rb').read()
    try:
        streams = heaps(data)
    except ValueError as e:
        print('%s.strings\tERR %s' % (label, e))
        return 0
    print('%s.streams\t%s' % (label, ' | '.join('%s (%d)' % (k, len(v)) for k, v in streams.items())))
    us = list(user_strings(streams.get('#US', b'\0')))
    print('%s.us\t%d' % (label, len(us)))
    for s in us:
        print('  |' + escape(s))
    names = [n.decode('utf-8', 'replace') for n in streams.get('#Strings', b'').split(b'\0') if n]
    print('%s.names\t%d' % (label, len(names)))
    for n in names:
        print('  |' + escape(n))
    return 0


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8', newline='\n')
    sys.exit(main(sys.argv))
