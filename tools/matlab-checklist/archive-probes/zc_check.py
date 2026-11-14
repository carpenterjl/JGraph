import struct, sys, zlib, binascii

def crc_update(crc, b):
    return binascii.crc32(bytes([b]), crc ^ 0xffffffff) ^ 0xffffffff

def keys_for(pw):
    k = [0x12345678, 0x23456789, 0x34567890]
    for c in pw:
        upd(k, c)
    return k

def upd(k, c):
    k[0] = crc_update(k[0], c)
    k[1] = (k[1] + (k[0] & 0xff)) & 0xffffffff
    k[1] = (k[1] * 134775813 + 1) & 0xffffffff
    k[2] = crc_update(k[2], (k[1] >> 24) & 0xff)

def dec(k, data):
    out = bytearray()
    for b in data:
        t = (k[2] | 2) & 0xffff
        c = b ^ (((t * (t ^ 1)) >> 8) & 0xff)
        upd(k, c)
        out.append(c)
    return bytes(out)

path, pwtext = sys.argv[1], sys.argv[2]
b = open(path, 'rb').read()
(sig, need, flags, method, mtime, mdate, crc, csize, usize, nlen, xlen) = struct.unpack('<IHHHHHIIIHH', b[:30])
eocd = b.rfind(b'PK\x05\x06')
cdoff = struct.unpack('<I', b[eocd + 16:eocd + 20])[0]
ccrc, ccsize = struct.unpack('<II', b[cdoff + 16:cdoff + 24])
data = b[30 + nlen + xlen:30 + nlen + xlen + ccsize]
for enc in ['utf-8', 'cp1252', 'utf-16-le']:
    pw = pwtext.encode(enc)
    k = keys_for(pw)
    h = dec(k, data[:12])
    check = (mtime >> 8) & 0xff if flags & 8 else ccrc >> 24
    ok = h[11] == check
    body = dec(k, data[12:])
    try:
        plain = zlib.decompress(body, -15)
        good = binascii.crc32(plain) == ccrc
    except Exception as e:
        good = False
    print(enc, 'check', ok, 'crc', good)
