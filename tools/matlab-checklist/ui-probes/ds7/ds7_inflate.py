"""DS7: rewrites a compressed level-5 (version 7) MAT-file uncompressed, so ds0_tree_dump.py can list
its subsystem objects. Each miCOMPRESSED (type 15) element is inflated in place; the header's
subsystem offset (bytes 116..123) is moved to where the element it named now starts.
Usage: python ds7_inflate.py in.mat out.mat   (prints 'v7.3' and exits 2 for an HDF5 file)"""
import struct
import sys
import zlib


def main(src, dst):
    buf = open(src, 'rb').read()
    if buf[:10] == b'MATLAB 7.3':
        print('v7.3 (HDF5): not a level-5 file')
        return 2
    head = bytearray(buf[:128])
    sub = struct.unpack_from('<Q', head, 116)[0]
    out = bytearray(head)
    at = 128
    new_sub = 0
    while at + 8 <= len(buf):
        t, n = struct.unpack_from('<II', buf, at)
        body = buf[at + 8: at + 8 + n]
        if at == sub:
            new_sub = len(out)
        if t == 15:
            out += zlib.decompress(body)
        else:
            out += buf[at: at + 8 + n]
        at += 8 + n
    if sub not in (0, 0x2020202020202020) and new_sub:
        struct.pack_into('<Q', out, 116, new_sub)
    open(dst, 'wb').write(bytes(out))
    print(f'inflated {len(buf)} -> {len(out)} bytes; subsystem {sub} -> {new_sub}')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1], sys.argv[2]))
