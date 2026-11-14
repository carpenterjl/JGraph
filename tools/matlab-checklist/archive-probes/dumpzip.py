import struct, sys

def dump(path):
    b = open(path, 'rb').read()
    eocd = b.rfind(b'PK\x05\x06')
    n, cdsize, cdoff = struct.unpack('<HII', b[eocd + 10:eocd + 20])
    print(f'== {path}: {len(b)} bytes, {n} entries, eocd comment len {struct.unpack("<H", b[eocd+20:eocd+22])[0]}')
    p = cdoff
    for i in range(n):
        (sig, vmade, vneed, flags, method, mtime, mdate, crc, csize, usize, nlen, xlen, clen, disk, iattr, eattr, loff) = struct.unpack('<IHHHHHHIIIHHHHHII', b[p:p + 46])
        name = b[p + 46:p + 46 + nlen].decode('utf-8', 'replace')
        extra = b[p + 46 + nlen:p + 46 + nlen + xlen]
        ex = parse_extra(extra)
        # local header
        (lsig, lvneed, lflags, lmethod, lmtime, lmdate, lcrc, lcsize, lusize, lnlen, lxlen) = struct.unpack('<IHHHHHIIIHH', b[loff:loff + 30])
        lextra = b[loff + 30 + lnlen:loff + 30 + lnlen + lxlen]
        dstart = loff + 30 + lnlen + lxlen
        head = b[dstart:dstart + 20].hex()
        dd = ''
        if lflags & 8:
            q = dstart + csize
            dd = b[q:q + 16].hex()
        print(f'  {name!r} made={vmade:#x} need={vneed} flags={flags:#06x} method={method} crc={crc:08x} csize={csize} usize={usize} iattr={iattr} eattr={eattr:#010x} extra={ex}')
        print(f'     local need={lvneed} flags={lflags:#06x} method={lmethod} crc={lcrc:08x} csize={lcsize} usize={lusize} lextra={parse_extra(lextra)} data[0:20]={head} dd={dd}')
        p += 46 + nlen + xlen + clen

def parse_extra(x):
    out = []
    i = 0
    while i + 4 <= len(x):
        hid, sz = struct.unpack('<HH', x[i:i + 4])
        d = x[i + 4:i + 4 + sz]
        if hid == 0x9901:
            ver, vend, strength, meth = struct.unpack('<H2sBH', d[:7])
            out.append(f'AES(v{ver},{vend},s{strength},m{meth})')
        else:
            out.append(f'{hid:#06x}:{d.hex()}')
        i += 4 + sz
    return out

for a in sys.argv[1:]:
    dump(a)
