"""Compare a built exe's PE part with the shipped launcher stub."""
import sys

exe, stub = sys.argv[1], sys.argv[2]
d = open(exe, 'rb').read()
s = open(stub, 'rb').read()
j = d.find(b'_xmlsignatures/sigpackagesig.xml')
start = d.rfind(b'PK\x03\x04', 0, j)
pe = d[:start]
print('exe PE part', len(pe), 'stub', len(s))
n = min(len(pe), len(s))
diffs = [k for k in range(n) if pe[k] != s[k]]
print('differing bytes in common prefix:', len(diffs))
if diffs:
    print('first diffs at', diffs[:20], 'last', diffs[-5:])
    for k in diffs[:8]:
        print('  @%d exe=%02x stub=%02x' % (k, pe[k], s[k]))
tail = pe[len(s):]
print('extra bytes after stub length:', len(tail), 'nonzero:', sum(1 for b in tail if b))
print('tail head:', tail[:64].hex(' '))
print('tail text:', tail[:200])
