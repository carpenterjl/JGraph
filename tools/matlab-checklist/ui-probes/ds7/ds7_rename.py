"""DS7: copies an .mlapp with one class name replaced by another of the SAME length in every part
(the model's MCOS names table, the class text, the metadata), so App Designer's reader meets a class
that was never loaded in the session. The model is written back inflated (uncompressed level 5,
which load reads). Usage: python ds7_rename.py in.mlapp out.mlapp OldName NewName"""
import struct
import sys
import zipfile
import zlib


def inflate(buf):
    head = bytearray(buf[:128])
    sub = struct.unpack_from('<Q', head, 116)[0]
    out = bytearray(head)
    at, new_sub = 128, 0
    while at + 8 <= len(buf):
        t, n = struct.unpack_from('<II', buf, at)
        if at == sub:
            new_sub = len(out)
        out += zlib.decompress(buf[at + 8: at + 8 + n]) if t == 15 else buf[at: at + 8 + n]
        at += 8 + n
    if new_sub:
        struct.pack_into('<Q', out, 116, new_sub)
    return bytes(out)


def main(src, dst, old, new):
    assert len(old) == len(new), 'names must have the same length'
    zin = zipfile.ZipFile(src)
    zout = zipfile.ZipFile(dst, 'w')
    counts = {}
    for info in zin.infolist():
        data = zin.read(info.filename)
        if info.filename.endswith('.mat'):
            if data[:10] != b'MATLAB 7.3':
                data = inflate(data)
        n = data.count(old.encode())
        data = data.replace(old.encode(), new.encode())
        counts[info.filename] = n
        zi = zipfile.ZipInfo(info.filename, info.date_time)
        zi.compress_type = info.compress_type
        zi.external_attr = info.external_attr
        zout.writestr(zi, data)
    zout.close()
    print('replaced', counts)


if __name__ == '__main__':
    main(*sys.argv[1:5])
