"""Compare the PE part (before the CTF overlay) of standalone executables, and look for a stub on disk."""
import hashlib
import sys
import os

def split(path):
    data = open(path, 'rb').read()
    i = data.find(b'PK\x03\x04_xmlsig'.replace(b'_xmlsig', b''))
    # first local header that starts the OPC package
    j = data.find(b'_xmlsignatures/sigpackagesig.xml')
    start = data.rfind(b'PK\x03\x04', 0, j)
    return data[:start], data[start:]

paths = sys.argv[1:]
for p in paths:
    pe, ov = split(p)
    print('%-70s pe=%8d sha=%s overlay=%d' % (os.path.relpath(p), len(pe), hashlib.sha256(pe).hexdigest()[:16], len(ov)))
# diff first two PE parts byte-wise
if len(paths) >= 2:
    a, _ = split(paths[0]); b, _ = split(paths[1])
    n = min(len(a), len(b))
    diffs = [k for k in range(n) if a[k] != b[k]]
    print('byte diffs between first two PE parts:', len(diffs), 'len a/b', len(a), len(b), 'first', diffs[:10])
