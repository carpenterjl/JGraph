"""Scan compiler outputs: magic bytes, zip signatures, PE overlay, zip listing."""
import struct
import sys
import zipfile
import io
import os


def pe_info(data):
    if data[:2] != b'MZ':
        return None
    pe = struct.unpack_from('<I', data, 0x3C)[0]
    if data[pe:pe + 4] != b'PE\0\0':
        return None
    machine, nsec = struct.unpack_from('<HH', data, pe + 4)
    opt_size = struct.unpack_from('<H', data, pe + 20)[0]
    chars = struct.unpack_from('<H', data, pe + 22)[0]
    opt = pe + 24
    magic = struct.unpack_from('<H', data, opt)[0]
    subsystem = struct.unpack_from('<H', data, opt + 68)[0]
    # data directories
    if magic == 0x20B:
        dd = opt + 112
    else:
        dd = opt + 96
    clr_rva, clr_size = struct.unpack_from('<II', data, dd + 14 * 8)
    cert_off, cert_size = struct.unpack_from('<II', data, dd + 4 * 8)
    sec = opt + opt_size
    end = 0
    secs = []
    for i in range(nsec):
        name = data[sec:sec + 8].rstrip(b'\0').decode('ascii', 'replace')
        vsize, vaddr, rawsize, rawptr = struct.unpack_from('<IIII', data, sec + 8)
        secs.append((name, vsize, vaddr, rawsize, rawptr))
        end = max(end, rawptr + rawsize)
        sec += 40
    return dict(machine=hex(machine), pe32plus=(magic == 0x20B), subsystem=subsystem,
                dll=bool(chars & 0x2000), clr=(clr_size > 0), cert=(cert_off, cert_size),
                sections=secs, end_of_sections=end, filesize=len(data))


def find_all(data, pat, limit=20):
    out = []
    i = data.find(pat)
    while i >= 0 and len(out) < limit:
        out.append(i)
        i = data.find(pat, i + 1)
    return out


def report(path):
    data = open(path, 'rb').read()
    print('=' * 70)
    print(path, len(data), 'bytes')
    print('head hex :', data[:48].hex(' '))
    print('head text:', data[:48])
    pi = pe_info(data)
    if pi:
        print('PE:', {k: v for k, v in pi.items() if k != 'sections'})
        for s in pi['sections']:
            print('   section', s)
        overlay = len(data) - pi['end_of_sections']
        print('overlay bytes after sections:', overlay)
        if overlay > 0:
            ov = data[pi['end_of_sections']:]
            print('overlay head hex :', ov[:64].hex(' '))
            print('overlay head text:', ov[:64])
            print('overlay tail hex :', ov[-64:].hex(' '))
    for pat in [b'PK\x03\x04', b'PK\x05\x06', b'PK\x01\x02', b'V2MCC', b'MATLAB_CTF', b'<?xml', b'manifest', b'.ctf']:
        hits = find_all(data, pat)
        print('  %-14r hits: %d %s' % (pat, len(hits), hits[:8]))
    # try zip on whole file
    try:
        z = zipfile.ZipFile(io.BytesIO(data))
        print('ZIP readable: %d entries' % len(z.infolist()))
        for zi in z.infolist()[:400]:
            print('   %9d %9d %s %s' % (zi.file_size, zi.compress_size, zi.compress_type, zi.filename))
        if len(z.infolist()) > 400:
            print('   ...')
    except Exception as e:
        print('ZIP not readable:', e)


if __name__ == '__main__':
    for p in sys.argv[1:]:
        report(p)
