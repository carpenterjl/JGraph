"""Call the compiled jgprobe package and report Python-side types."""
import io
import sys
import time


def describe(v, depth=0):
    pad = '  ' * depth
    t = type(v)
    extra = ''
    if hasattr(v, 'size'):
        try:
            extra += ' size=%r' % (v.size,)
        except Exception:
            pass
    if hasattr(v, '_is_complex'):
        extra += ' complex=%r' % v._is_complex
    print('%s%s.%s%s repr=%r' % (pad, t.__module__, t.__name__, extra, v))
    if isinstance(v, dict):
        for k, x in v.items():
            print('%s  key %r:' % (pad, k))
            describe(x, depth + 2)
    elif isinstance(v, (list, tuple)):
        for x in v:
            describe(x, depth + 1)


t0 = time.time()
import jgprobe
import matlab
t1 = time.time()
print('import jgprobe: %.2f s; jgprobe file=%s' % (t1 - t0, jgprobe.__file__))
print('matlab module file=%s' % matlab.__file__)
h = jgprobe.initialize()
t2 = time.time()
print('initialize(): %.2f s; handle type=%s' % (t2 - t1, type(h)))

print('--- addtwo(1.0, 2.0)')
r = h.addtwo(1.0, 2.0)
describe(r)
print('--- addtwo(1, 2)  (python ints)')
try:
    r = h.addtwo(1, 2)
    describe(r)
except Exception as e:
    print('error', type(e).__name__, e)
print('--- addtwo(matlab.double([[1,2,3]]), 10.0)')
r = h.addtwo(matlab.double([[1, 2, 3]]), 10.0)
describe(r)
print('--- addtwo("3", "4")  (str args)')
try:
    r = h.addtwo('3', '4')
    describe(r)
except Exception as e:
    print('error', type(e).__name__, e)
print('--- addtwo with stdout capture')
out = io.StringIO()
r = h.addtwo(5.0, 6.0, stdout=out)
print('captured stdout=%r' % out.getvalue())
describe(r)
print('--- addtwo(1.0, 2.0, nargout=0)')
r = h.addtwo(1.0, 2.0, nargout=0)
describe(r)
print('--- multi(1.0, 2.0, 3.0, nargout=3)')
r = h.multi(1.0, 2.0, 3.0, nargout=3)
describe(r)
print('--- multi(1.0, 2.0) default nargout')
r = h.multi(1.0, 2.0)
describe(r)
print('--- shapes(nargout=6)')
r = h.shapes(nargout=6)
describe(r)
print('--- error path: addtwo with 3 args')
try:
    h.addtwo(1.0, 2.0, 3.0)
except Exception as e:
    print('exception type=%s.%s msg=%r' % (type(e).__module__, type(e).__name__, str(e)))
print('--- error path: unknown function')
try:
    h.nosuchfunc(1.0)
except Exception as e:
    print('exception type=%s.%s msg=%r' % (type(e).__module__, type(e).__name__, str(e)))
print('--- background call')
f = h.multi(4.0, 5.0, nargout=3, background=True)
print('future type=%s' % type(f))
describe(f.result())
h.terminate()
print('terminated; total %.2f s' % (time.time() - t0))
