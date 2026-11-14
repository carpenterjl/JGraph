"""Assembles a zip from entries of other zips: mixzip.py out.zip src1.zip:name1 src2.zip:name2 ..."""
import struct, sys

def entries(path):
    b = open(path, 'rb').read()
    eocd = b.rfind(b'PK\x05\x06')
    n, cdsize, cdoff = struct.unpack('<HII', b[eocd + 10:eocd + 20])
    out = {}
    p = cdoff
    starts = []
    for i in range(n):
        nlen, xlen, clen = struct.unpack('<HHH', b[p + 28:p + 34])
        loff = struct.unpack('<I', b[p + 42:p + 46])[0]
        name = b[p + 46:p + 46 + nlen].decode('utf-8')
        out[name] = (b[p:p + 46 + nlen + xlen + clen], loff)
        starts.append(loff)
        p += 46 + nlen + xlen + clen
    starts.append(cdoff)
    starts.sort()
    res = {}
    for name, (central, loff) in out.items():
        end = next(s for s in starts if s > loff)
        res[name] = (central, b[loff:end])
    return res

out = sys.argv[1]
local = b''
central = b''
count = 0
for spec in sys.argv[2:]:
    path, name = spec.split(':', 1)
    c, l = entries(path)[name]
    c = c[:42] + struct.pack('<I', len(local)) + c[46:]
    central += c
    local += l
    count += 1
eocd = b'PK\x05\x06' + struct.pack('<HHHHIIH', 0, 0, count, count, len(central), len(local), 0)
open(out, 'wb').write(local + central + eocd)
print('wrote', out, count)
