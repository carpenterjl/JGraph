"""List a PE file's imported DLLs and whether each resolves on the given search dirs (argv[2:])."""
import os
import struct
import sys

path = sys.argv[1]
dirs = sys.argv[2:] + [os.path.dirname(os.path.abspath(path)), r'C:\Windows\System32']
d = open(path, 'rb').read()
pe = struct.unpack_from('<I', d, 0x3C)[0]
nsec = struct.unpack_from('<H', d, pe + 6)[0]
optsz = struct.unpack_from('<H', d, pe + 20)[0]
opt = pe + 24
magic = struct.unpack_from('<H', d, opt)[0]
dd = opt + (112 if magic == 0x20B else 96)
imp_rva = struct.unpack_from('<I', d, dd + 8)[0]
secs = []
s = opt + optsz
for i in range(nsec):
    vs, va, rs, rp = struct.unpack_from('<IIII', d, s + 8)
    secs.append((va, max(vs, rs), rp))
    s += 40

def off(rva):
    for va, sz, rp in secs:
        if va <= rva < va + sz:
            return rva - va + rp
    raise ValueError(rva)

o = off(imp_rva)
missing = []
while True:
    name_rva = struct.unpack_from('<I', d, o + 12)[0]
    if name_rva == 0:
        break
    n = off(name_rva)
    name = d[n:d.index(b'\0', n)].decode()
    found = name.lower().startswith(('api-ms-', 'ext-ms-')) or any(os.path.isfile(os.path.join(x, name)) for x in dirs)
    print('%-40s %s' % (name, 'ok' if found else 'MISSING'))
    if not found:
        missing.append(name)
    o += 20
print('MISSING:' if missing else 'all imports resolve', ' '.join(missing))
sys.exit(1 if missing else 0)
