"""A Python client of stage K13's package k13pkg (compiler and SDK plan), run unchanged against
R2025b's build (record-k13.ps1) and JGraph's (run-k13.ps1). Each case prints one line: its name, a
tab, and what came back - a value's type and repr, or ERR with the exception's type and message.

Modes (the first argument):
  arrays         the matlab module alone: constructors, attributes, methods, indexing, buffers
  calls          a package's calls: names, keywords, outputs, text, errors, objects
  convert        values out (k9make), in (k9describe) and round trips (k9echo)
  background     background calls and FutureResult
  life           a second instance, terminate, exit, quit, terminate_runtime
  runtime CASE   initialize_runtime's cases, one process each
  version M N    the package imported as if Python were M.N
"""
import array
import collections
import copy
import io
import re
import sys
import time
import warnings

MODE = sys.argv[1] if len(sys.argv) > 1 else 'arrays'
ARGS = sys.argv[2:]

if MODE == 'version':
    # What the package's __init__ imports, imported before the version is replaced.
    import atexit, glob, importlib, os, pdb, pkgutil, platform, weakref  # noqa: E401,F401
    _real = sys.version_info
    sys.version_info = collections.namedtuple('version_info', 'major minor micro releaselevel serial')(
        int(ARGS[0]), int(ARGS[1]), 0, 'final', 0)

warnings.simplefilter('always')
_caught = []
warnings.showwarning = lambda message, category, filename, lineno, file=None, line=None: _caught.append(
    '%s: %s' % (category.__name__, message))


class Raw(object):
    """An answer printed as it is."""

    def __init__(self, text):
        self.text = text


def say(name, text):
    print('%s\t%s' % (name, text), flush=True)


def tname(v):
    t = type(v)
    return t.__name__ if t.__module__ == 'builtins' else '%s.%s' % (t.__module__, t.__name__)


def addr(text):
    return re.sub(r'0x[0-9A-Fa-f]+', '0x<ADDR>', text)


def desc(v):
    """A value as a line: its type, and its parts' types inside lists, tuples and dicts."""
    if isinstance(v, Raw):
        return v.text
    if v is None:
        return 'None'
    if isinstance(v, list):
        return 'list[' + ', '.join(desc(x) for x in v) + ']'
    if isinstance(v, tuple):
        return 'tuple(' + ', '.join(desc(x) for x in v) + ')'
    if isinstance(v, dict):
        return 'dict{' + ', '.join('%r: %s' % (k, desc(x)) for k, x in v.items()) + '}'
    if isinstance(v, (bool, int, float, complex, str, bytes)):
        return '%s %r' % (tname(v), v)
    if hasattr(v, '_is_complex') and hasattr(v, 'size'):
        return '%s size=%r %s' % (tname(v), v.size, addr(repr(v)))
    return '%s %s' % (tname(v), addr(repr(v)))


def err(e):
    return 'ERR %s | %s' % (tname(e), addr(str(e)).replace('\r', '').replace('\n', '\\n'))


def run(name, fn):
    del _caught[:]
    try:
        text = desc(fn())
    except BaseException as e:  # SystemExit and KeyboardInterrupt are answers too
        text = err(e)
    if _caught:
        text += '  WARN ' + ' / '.join(_caught)
        del _caught[:]
    say(name, text)


def mut(make, action):
    """An action on a fresh value: the action's answer, then the value after it."""
    def go():
        x = make()
        try:
            answer = desc(action(x))
        except BaseException as e:
            answer = err(e)
        return Raw('%s ;; after %s' % (answer, desc(x)))
    return go


def caught(fn):
    """An exception's line, or the answer when there is none."""
    try:
        return fn()
    except BaseException as e:
        return Raw(err(e))


try:
    import numpy
except ImportError:
    numpy = None


def np(fn):
    def go():
        return fn() if numpy is not None else Raw('skip (no numpy)')
    return go


def view(m):
    """What a memoryview says about itself."""
    return Raw('format=%r itemsize=%r ndim=%r shape=%r strides=%r readonly=%r c=%r f=%r nbytes=%r list=%r' % (
        m.format, m.itemsize, m.ndim, m.shape, m.strides, m.readonly, m.c_contiguous, m.f_contiguous, m.nbytes,
        m.tolist()))


def nd(a):
    """What numpy makes of an array."""
    x = numpy.asarray(a)
    return Raw('dtype=%s shape=%r c=%r f=%r list=%r' % (x.dtype, x.shape, x.flags['C_CONTIGUOUS'],
                                                        x.flags['F_CONTIGUOUS'], x.tolist()))


# --- the matlab module -------------------------------------------------------------------------

CLASSES = ('double', 'single', 'int8', 'uint8', 'int16', 'uint16', 'int32', 'uint32', 'int64', 'uint64', 'logical')


def which(fn):
    """Which exception classes an error belongs to."""
    import matlab
    try:
        fn()
        return Raw('no error')
    except Exception as e:
        return Raw('%s shape=%r size=%r value=%r type=%r' % (
            tname(e), isinstance(e, matlab.ShapeError), isinstance(e, matlab.SizeError), isinstance(e, ValueError),
            isinstance(e, TypeError)))


def mode_arrays():
    import k13pkg  # noqa: F401 - the package puts the matlab module on the path
    import matlab
    say('module.names', ' '.join(n for n in CLASSES + ('ShapeError', 'SizeError', 'object', 'engine', 'mlarray', 'mlexceptions')
                                 if hasattr(matlab, n)))
    for n in CLASSES:
        c = getattr(matlab, n)
        say('class.' + n, '%s.%s mro=%s public=%s' % (c.__module__, c.__name__, [k.__name__ for k in c.__mro__],
                                                     ' '.join(sorted(a for a in dir(c) if not a.startswith('__')))))
    for n in ('ShapeError', 'SizeError'):
        c = getattr(matlab, n)
        say('class.' + n, '%s.%s mro=%s' % (c.__module__, c.__name__, [k.__name__ for k in c.__mro__]))
    say('class.names', '%s %s %s' % (matlab.double.__module__, matlab.double.__name__, matlab.double.__qualname__))
    say('class.hash', repr(matlab.double.__hash__))

    d = matlab.double
    i8 = matlab.int8
    cases = [
        ('new.none', lambda: d()),
        ('new.emptylist', lambda: d([])),
        ('new.emptyrow', lambda: d([[]])),
        ('new.emptyrows', lambda: d([[], []])),
        ('new.float', lambda: d(1.5)),
        ('new.int', lambda: d(3)),
        ('new.bool', lambda: d(True)),
        ('new.complex', lambda: d(1 + 2j)),
        ('new.str', lambda: d('ab')),
        ('new.None', lambda: d(None)),
        ('new.flat', lambda: d([1, 2, 3])),
        ('new.row', lambda: d([[1, 2, 3]])),
        ('new.column', lambda: d([[1], [2], [3]])),
        ('new.ragged', lambda: d([[1, 2], [3]])),
        ('new.mixednest', lambda: d([[1, 2], 3])),
        ('new.mixednest2', lambda: d([1, [2, 3]])),
        ('new.cube', lambda: d([[[1, 2], [3, 4]], [[5, 6], [7, 8]]])),
        ('new.tuple', lambda: d((1, 2, 3))),
        ('new.tuples', lambda: d(((1, 2), (3, 4)))),
        ('new.range', lambda: d(range(3))),
        ('new.generator', lambda: d(x for x in [1, 2])),
        ('new.arrayd', lambda: d(array.array('d', [1.5, 2.5]))),
        ('new.arrayi', lambda: d(array.array('i', [1, 2]))),
        ('new.bytes', lambda: d(b'ab')),
        ('new.bytearray', lambda: d(bytearray(b'ab'))),
        ('new.memoryview', lambda: d(memoryview(array.array('d', [1.0, 2.0])))),
        ('new.from.int8', lambda: d(i8([1, 2]))),
        ('new.from.double', lambda: d(d([[1, 2], [3, 4]]))),
        ('new.from.row', lambda: d(d([[1, 2], [3, 4]])[1])),
        ('new.bools', lambda: d([True, False])),
        ('new.strelement', lambda: d([1, 'a'])),
        ('new.Noneelement', lambda: d([1, None])),
        ('new.complexelement', lambda: d([1 + 2j])),
        ('new.cplx.real', lambda: d([1, 2], is_complex=True)),
        ('new.cplx.mixed', lambda: d([1 + 2j, 3], is_complex=True)),
        ('new.cplx.matrix', lambda: d([[1 + 2j, 3], [4, 5j]], is_complex=True)),
        ('new.cplx.false', lambda: d([1 + 2j], is_complex=False)),
        ('new.cplx.notbool', lambda: d([1], is_complex=1)),
        ('new.size', lambda: d(size=(2, 3))),
        ('new.size.cplx', lambda: d(size=(2, 2), is_complex=True)),
        ('new.size.flat23', lambda: d([1, 2, 3, 4, 5, 6], size=(2, 3))),
        ('new.size.flat32', lambda: d([1, 2, 3, 4, 5, 6], size=(3, 2))),
        ('new.size.flatcube', lambda: d(list(range(1, 9)), size=(2, 2, 2))),
        ('new.size.mismatch', lambda: d([1, 2, 3], size=(2, 2))),
        ('new.size.nested', lambda: d([[1, 2], [3, 4]], size=(4, 1))),
        ('new.size.nestedsame', lambda: d([[1, 2], [3, 4]], size=(2, 2))),
        ('new.size.list', lambda: d(size=[2, 3])),
        ('new.size.one', lambda: d(size=(2,))),
        ('new.size.zero', lambda: d(size=(0, 3))),
        ('new.size.negative', lambda: d(size=(-1, 2))),
        ('new.size.float', lambda: d(size=(2.5, 1))),
        ('new.size.int', lambda: d(size=3)),
        ('new.size.three', lambda: d(size=(2, 3, 4))),
        ('new.size.trailing', lambda: d(size=(1, 1, 1))),
        ('new.size.trailing2', lambda: d(size=(2, 3, 1))),
        ('new.size.empty', lambda: d(size=())),
        ('new.size.str', lambda: d(size='ab')),
        ('new.positional.size', lambda: d([1, 2], (1, 2))),
        ('new.positional.cplx', lambda: d([1, 2], (1, 2), True)),
        ('new.keyword.initializer', lambda: d(initializer=[1, 2])),
        ('new.keyword.bogus', lambda: d([1], bogus=1)),
        ('new.nan', lambda: d([float('nan'), float('inf'), -float('inf')])),
        ('new.negzero', lambda: d([-0.0])),
        ('new.bigint', lambda: d([2 ** 53 + 1])),
        ('new.hugeint', lambda: d([2 ** 70])),
        ('new.np.matrix', np(lambda: d(numpy.array([[1., 2.], [3., 4.]])))),
        ('new.np.int32', np(lambda: d(numpy.array([1, 2], dtype=numpy.int32)))),
        ('new.np.fortran', np(lambda: d(numpy.asfortranarray(numpy.arange(6.).reshape(2, 3))))),
        ('new.np.cube', np(lambda: d(numpy.arange(8.).reshape(2, 2, 2)))),
        ('new.np.complex', np(lambda: d(numpy.array([1 + 2j])))),
        ('new.np.complex.flag', np(lambda: d(numpy.array([1 + 2j]), is_complex=True))),
        ('new.np.scalar', np(lambda: d(numpy.float64(2.5)))),
        ('new.np.scalars', np(lambda: d([numpy.float64(1.5), 2]))),
        ('new.np.strided', np(lambda: d(numpy.array([[1., 2.], [3., 4.]])[:, 0]))),
        ('new.np.bool', np(lambda: d(numpy.array([True, False])))),
        ('int8.ok', lambda: i8([1, 2, 3])),
        ('int8.limits', lambda: i8([127, -128])),
        ('int8.over', lambda: i8([128])),
        ('int8.under', lambda: i8([-129])),
        ('int8.fraction', lambda: i8([1.5])),
        ('int8.whole', lambda: i8([1.0])),
        ('int8.nan', lambda: i8([float('nan')])),
        ('int8.bool', lambda: i8([True])),
        ('int8.complexzero', lambda: i8([1 + 0j])),
        ('int8.cplx', lambda: i8([1, 2], is_complex=True)),
        ('int8.cplx.values', lambda: i8([1 + 2j], is_complex=True)),
        ('int8.np', np(lambda: i8(numpy.array([1, 2], dtype=numpy.int8)))),
        ('int8.np.wide', np(lambda: i8(numpy.array([300])))),
        ('uint8.negative', lambda: matlab.uint8([-1])),
        ('uint8.max', lambda: matlab.uint8([255])),
        ('uint8.over', lambda: matlab.uint8([256])),
        ('uint8.bytes', lambda: matlab.uint8(b'ab')),
        ('int16.over', lambda: matlab.int16([32768])),
        ('uint16.max', lambda: matlab.uint16([65535])),
        ('int32.over', lambda: matlab.int32([2 ** 31])),
        ('uint32.max', lambda: matlab.uint32([2 ** 32 - 1])),
        ('int64.limits', lambda: matlab.int64([2 ** 63 - 1, -2 ** 63])),
        ('int64.over', lambda: matlab.int64([2 ** 63])),
        ('uint64.max', lambda: matlab.uint64([2 ** 64 - 1])),
        ('uint64.over', lambda: matlab.uint64([2 ** 64])),
        ('uint64.negative', lambda: matlab.uint64([-1])),
        ('int64.size', lambda: matlab.int64(size=(1, 2))),
        ('single.ok', lambda: matlab.single([1.5, 2.25])),
        ('single.over', lambda: matlab.single([1e40])),
        ('single.round', lambda: matlab.single([0.1])),
        ('single.cplx', lambda: matlab.single([1 + 2j], is_complex=True)),
        ('logical.ok', lambda: matlab.logical([True, False])),
        ('logical.ints', lambda: matlab.logical([1, 0, 2])),
        ('logical.fraction', lambda: matlab.logical([1.5])),
        ('logical.column', lambda: matlab.logical([[True], [False]])),
        ('logical.cplx', lambda: matlab.logical([1], is_complex=True)),
        ('logical.size', lambda: matlab.logical(size=(2, 2))),
        ('logical.str', lambda: matlab.logical(['a'])),
        ('logical.np', np(lambda: matlab.logical(numpy.array([True, False])))),
    ]
    for name, fn in cases:
        run(name, fn)

    def a23():
        return d([[1, 2, 3], [4, 5, 6]])

    def c12():
        return d([[1 + 2j, 3 - 4j]], is_complex=True)

    def cube():
        return d([[[1, 2], [3, 4]], [[5, 6], [7, 8]]])

    attrs = [
        ('attr.size', lambda: a23().size),
        ('attr.size.type', lambda: Raw(tname(a23().size))),
        ('attr.itemsize', lambda: a23().itemsize),
        ('attr.itemsize.int8', lambda: i8([1]).itemsize),
        ('attr.itemsize.int16', lambda: matlab.int16([1]).itemsize),
        ('attr.itemsize.single', lambda: matlab.single([1]).itemsize),
        ('attr.itemsize.logical', lambda: matlab.logical([True]).itemsize),
        ('attr.itemsize.complex', lambda: c12().itemsize),
        ('attr.itemsize.csingle', lambda: matlab.single([1j], is_complex=True).itemsize),
        ('attr._is_complex', lambda: a23()._is_complex),
        ('attr._is_complex.c', lambda: c12()._is_complex),
        ('attr._size', lambda: a23()._size),
        ('attr._data', lambda: a23()._data),
        ('attr._data.c', lambda: c12()._data),
        ('attr._real', lambda: a23()._real),
        ('attr._real.c', lambda: c12()._real),
        ('attr._imag.c', lambda: c12()._imag),
        ('attr.strides', lambda: a23().get_bytewise_strides()),
        ('attr.strides.c', lambda: c12().get_bytewise_strides()),
        ('attr.strides.cube', lambda: d(size=(2, 3, 4)).get_bytewise_strides()),
        ('attr.strides.int16', lambda: matlab.int16([[1, 2], [3, 4]]).get_bytewise_strides()),
        ('attr.strides.row', lambda: a23()[1].get_bytewise_strides()),
        ('attr.buffer_info.count', lambda: a23().buffer_info()[1]),
        ('attr.buffer_info.count.c', lambda: c12().buffer_info()[1]),
        ('attr.buffer_info.count.row', lambda: a23()[1].buffer_info()[1]),
        ('attr.buffer_info.type', lambda: Raw(tname(a23().buffer_info()[0]))),
        ('attr.size.set', mut(a23, lambda x: setattr(x, 'size', (3, 2)))),
        ('attr.new', mut(a23, lambda x: setattr(x, 'extra', 1))),
    ]
    for name, fn in attrs:
        run(name, fn)

    methods = [
        ('reshape.args', mut(a23, lambda x: x.reshape(3, 2))),
        ('reshape.tuple', mut(a23, lambda x: x.reshape((3, 2)))),
        ('reshape.list', mut(a23, lambda x: x.reshape([6, 1]))),
        ('reshape.one', mut(a23, lambda x: x.reshape(6))),
        ('reshape.three', mut(a23, lambda x: x.reshape(1, 2, 3))),
        ('reshape.trailing', mut(a23, lambda x: x.reshape(6, 1, 1))),
        ('reshape.mismatch', mut(a23, lambda x: x.reshape(4, 2))),
        ('reshape.negative', mut(a23, lambda x: x.reshape(-1, 2))),
        ('reshape.float', mut(a23, lambda x: x.reshape(3.0, 2))),
        ('reshape.none', mut(a23, lambda x: x.reshape())),
        ('reshape.empty', mut(lambda: d([]), lambda x: x.reshape(0, 6))),
        ('reshape.view', mut(a23, lambda x: x[0].reshape(3, 1))),
        ('real.real', lambda: a23().real()),
        ('imag.real', lambda: a23().imag()),
        ('real.complex', lambda: c12().real()),
        ('imag.complex', lambda: c12().imag()),
        ('noncomplex.real', lambda: a23().noncomplex()),
        ('noncomplex.complex', lambda: c12().noncomplex()),
        ('toarray.real', lambda: a23().toarray()),
        ('toarray.complex', lambda: c12().toarray()),
        ('toarray.int8', lambda: i8([[1, 2]]).toarray()),
        ('toarray.uint64', lambda: matlab.uint64([[1, 2]]).toarray()),
        ('toarray.logical', lambda: matlab.logical([[True, False]]).toarray()),
        ('toarray.single', lambda: matlab.single([[1.5]]).toarray()),
        ('toarray.row', lambda: a23()[1].toarray()),
        ('tomemoryview.real', lambda: view(a23().tomemoryview())),
        ('tomemoryview.complex', lambda: view(c12().tomemoryview())),
        ('tomemoryview.int8', lambda: view(i8([[1, 2], [3, 4]]).tomemoryview())),
        ('tomemoryview.logical', lambda: view(matlab.logical([[True, False]]).tomemoryview())),
        ('tomemoryview.row', lambda: view(a23()[1].tomemoryview())),
        ('tomemoryview.cube', lambda: view(d(list(range(1, 9)), size=(2, 2, 2)).tomemoryview())),
        ('tomemoryview.empty', lambda: view(d([]).tomemoryview())),
        ('memoryview.real', lambda: view(memoryview(a23()))),
        ('memoryview.complex', lambda: view(memoryview(c12()))),
        ('memoryview.int8', lambda: view(memoryview(i8([[1, 2], [3, 4]])))),
        ('memoryview.uint64', lambda: view(memoryview(matlab.uint64([[1, 2]])))),
        ('memoryview.single', lambda: view(memoryview(matlab.single([[1.5, 2.5]])))),
        ('memoryview.logical', lambda: view(memoryview(matlab.logical([[True, False]])))),
        ('memoryview.row', lambda: view(memoryview(a23()[1]))),
        ('memoryview.empty', lambda: view(memoryview(d([])))),
        ('memoryview.write', mut(a23, lambda x: memoryview(x).__setitem__((0, 0), 9.0))),
        ('tomemoryview.write', mut(a23, lambda x: x.tomemoryview().__setitem__((0, 0), 9.0))),
        ('clone', mut(a23, lambda x: (lambda y: (y[0].__setitem__(0, 9), y)[1])(x.clone()))),
        ('clone.row', lambda: a23()[1].clone()),
        ('numpy.real', np(lambda: nd(a23()))),
        ('numpy.complex', np(lambda: nd(c12()))),
        ('numpy.cube', np(lambda: nd(d(list(range(1, 9)), size=(2, 2, 2))))),
        ('numpy.int8', np(lambda: nd(i8([[1, 2], [3, 4]])))),
        ('numpy.logical', np(lambda: nd(matlab.logical([[True, False]])))),
        ('numpy.row', np(lambda: nd(a23()[1]))),
        ('numpy.shares', np(mut(a23, lambda x: numpy.asarray(x).__setitem__((0, 0), 9.0)))),
        ('roundtrip.memoryview', lambda: d(a23().tomemoryview())),
        ('roundtrip.numpy', np(lambda: d(numpy.asarray(a23())))),
    ]
    for name, fn in methods:
        run(name, fn)

    index = [
        ('len', lambda: len(a23())),
        ('get.0', lambda: a23()[0]),
        ('get.1', lambda: a23()[1]),
        ('get.2', lambda: a23()[2]),
        ('get.-1', lambda: a23()[-1]),
        ('get.-3', lambda: a23()[-3]),
        ('get.0.1', lambda: a23()[0][1]),
        ('get.0.-1', lambda: a23()[0][-1]),
        ('get.0.3', lambda: a23()[0][3]),
        ('get.tuple', lambda: a23()[0, 1]),
        ('get.slice', lambda: a23()[0:1]),
        ('get.slice.all', lambda: a23()[:]),
        ('get.slice.step', lambda: a23()[::2]),
        ('get.colon', lambda: a23()[:, 1]),
        ('get.row.slice', lambda: a23()[0][0:2]),
        ('get.row.step', lambda: a23()[0][::2]),
        ('get.row.reverse', lambda: a23()[0][::-1]),
        ('get.str', lambda: a23()['x']),
        ('get.float', lambda: a23()[1.0]),
        ('get.bool', lambda: a23()[True]),
        ('get.np.int', np(lambda: a23()[numpy.int64(1)])),
        ('iter', lambda: list(a23())),
        ('iter.rows', lambda: [list(x) for x in a23()]),
        ('iter.type', lambda: Raw(tname(iter(a23())))),
        ('flat.len', lambda: len(d([1, 2, 3]))),
        ('flat.0', lambda: d([1, 2, 3])[0]),
        ('flat.0.2', lambda: d([1, 2, 3])[0][2]),
        ('flat.1', lambda: d([1, 2, 3])[1]),
        ('flat.iter', lambda: list(d([1, 2, 3]))),
        ('scalar.len', lambda: len(d(5.0))),
        ('scalar.0', lambda: d(5.0)[0]),
        ('scalar.0.0', lambda: d(5.0)[0][0]),
        ('empty.len', lambda: len(d([]))),
        ('empty.0', lambda: d([])[0]),
        ('empty.iter', lambda: list(d([]))),
        ('empty03.len', lambda: len(d(size=(0, 3)))),
        ('empty30.len', lambda: len(d(size=(3, 0)))),
        ('empty30.0', lambda: d(size=(3, 0))[0]),
        ('cube.len', lambda: len(cube())),
        ('cube.0', lambda: cube()[0]),
        ('cube.1.0.1', lambda: cube()[1][0][1]),
        ('cube.0.1', lambda: cube()[0][1]),
        ('complex.get', lambda: c12()[0][0]),
        ('int8.get', lambda: i8([[1, 2]])[0][1]),
        ('uint64.get', lambda: matlab.uint64([[2 ** 64 - 1]])[0][0]),
        ('logical.get', lambda: matlab.logical([[True, False]])[0][1]),
        ('single.get', lambda: matlab.single([[1.5]])[0][0]),
        ('single.get.round', lambda: matlab.single([[0.1]])[0][0]),
        ('set.element', mut(a23, lambda x: x[0].__setitem__(1, 9))),
        ('set.row', mut(a23, lambda x: x.__setitem__(0, [7, 8, 9]))),
        ('set.row.short', mut(a23, lambda x: x.__setitem__(0, [1, 2]))),
        ('set.row.scalar', mut(a23, lambda x: x.__setitem__(0, 5))),
        ('set.row.matlab', mut(a23, lambda x: x.__setitem__(0, d([7, 8, 9])))),
        ('set.str', mut(a23, lambda x: x[0].__setitem__(0, 'x'))),
        ('set.None', mut(a23, lambda x: x[0].__setitem__(0, None))),
        ('set.slice', mut(a23, lambda x: x[0].__setitem__(slice(0, 2), [10, 11]))),
        ('set.slice.short', mut(a23, lambda x: x[0].__setitem__(slice(0, 2), [10]))),
        ('set.view', mut(a23, lambda x: (lambda v: v.__setitem__(0, 99))(x[0]))),
        ('set.complex.into.real', mut(a23, lambda x: x[0].__setitem__(0, 1 + 2j))),
        ('set.real.into.complex', mut(c12, lambda x: x[0].__setitem__(0, 5))),
        ('set.int8.over', mut(lambda: i8([[1, 2]]), lambda x: x[0].__setitem__(0, 300))),
        ('set.int8.fraction', mut(lambda: i8([[1, 2]]), lambda x: x[0].__setitem__(0, 1.5))),
        ('set.logical.five', mut(lambda: matlab.logical([[True, False]]), lambda x: x[0].__setitem__(0, 5))),
        ('set.outside', mut(a23, lambda x: x.__setitem__(5, [1, 2, 3]))),
        ('set.tuple', mut(a23, lambda x: x.__setitem__((0, 1), 9))),
        ('set.flat', mut(lambda: d([1, 2, 3]), lambda x: x.__setitem__(0, [4, 5, 6]))),
        ('set.flat.element', mut(lambda: d([1, 2, 3]), lambda x: x[0].__setitem__(1, 9))),
        # del of an element ends R2025b's process (0xC0000005), so only a row's is asked.
        ('del.row', mut(a23, lambda x: x.__delitem__(0))),
    ]
    for name, fn in index:
        run(name, fn)

    compare = [
        ('eq.same', lambda: d([1, 2]) == d([1, 2])),
        ('eq.other', lambda: d([1, 2]) == d([1, 3])),
        ('eq.list', lambda: d([1, 2]) == [1, 2]),
        ('eq.list2', lambda: d([[1, 2]]) == [[1, 2]]),
        ('eq.class', lambda: d([1, 2]) == i8([1, 2])),
        ('eq.shape', lambda: d([[1, 2]]) == d([[1], [2]])),
        ('eq.flatrow', lambda: d([1, 2]) == d([[1, 2]])),
        ('eq.row.view', lambda: d([[1, 2], [3, 4]])[0] == d([1, 2])),
        ('eq.complex', lambda: d([1], is_complex=True) == d([1])),
        ('eq.nan', lambda: d([float('nan')]) == d([float('nan')])),
        ('ne.same', lambda: d([1, 2]) != d([1, 2])),
        ('lt', lambda: d([1]) < d([2])),
        ('hash', lambda: hash(d([1]))),
        ('bool.one', lambda: bool(d([1]))),
        ('bool.empty', lambda: bool(d([]))),
        ('bool.zero', lambda: bool(d([0]))),
        ('contains', lambda: 2 in d([1, 2, 3])),
        ('contains.row', lambda: 2 in d([[1, 2, 3]])),
        ('add', lambda: d([1]) + d([2])),
        ('copy', mut(lambda: d([[1, 2]]), lambda x: (lambda y: (y[0].__setitem__(0, 9), y)[1])(copy.copy(x)))),
        ('deepcopy', mut(lambda: d([[1, 2]]), lambda x: (lambda y: (y[0].__setitem__(0, 9), y)[1])(copy.deepcopy(x)))),
        ('pickle', lambda: __import__('pickle').loads(__import__('pickle').dumps(d([[1, 2]])))),
        ('sequence', lambda: isinstance(d([1]), collections.abc.Sequence)),
        ('repr.matrix', lambda: Raw(repr(a23()))),
        ('repr.empty', lambda: Raw(repr(d([])))),
        ('repr.empty03', lambda: Raw(repr(d(size=(0, 3))))),
        ('repr.scalar', lambda: Raw(repr(d(5)))),
        ('repr.complex', lambda: Raw(repr(c12()))),
        ('repr.complexmatrix', lambda: Raw(repr(d([[1 + 2j, 3], [4, 5j]], is_complex=True)))),
        ('repr.int8', lambda: Raw(repr(i8([[1, -2]])))),
        ('repr.uint64', lambda: Raw(repr(matlab.uint64([[2 ** 64 - 1]])))),
        ('repr.logical', lambda: Raw(repr(matlab.logical([[True, False]])))),
        ('repr.single', lambda: Raw(repr(matlab.single([[0.1, 1.5]])))),
        ('repr.cube', lambda: Raw(repr(cube()))),
        ('repr.flat', lambda: Raw(repr(d([1, 2, 3])))),
        ('repr.view', lambda: Raw(repr(a23()[1]))),
        ('repr.floats', lambda: Raw(repr(d([[0.1, 1e20, 1e-7, 123456789.0, float('nan'), -float('inf'), -0.0, 1e16, 2.5e-300]])))),
        ('repr.long', lambda: Raw(repr(d(list(range(30)))))),
        ('repr.subclass', lambda: Raw(repr(type('mine', (d,), {})([1])))),
        ('str.matrix', lambda: Raw(str(a23()))),
        ('str.empty', lambda: Raw(str(d([])))),
        ('str.complex', lambda: Raw(str(c12()))),
        ('str.logical', lambda: Raw(str(matlab.logical([[True, False]])))),
        ('str.int8', lambda: Raw(str(i8([[1, -2]])))),
        ('str.cube', lambda: Raw(str(cube()))),
        ('str.flat', lambda: Raw(str(d([1, 2, 3])))),
        ('format', lambda: Raw('%s|%r' % (d([[1, 2]]), d([[1, 2]])))),
    ]
    for name, fn in compare:
        run(name, fn)

    errors = [
        ('which.ragged', lambda: which(lambda: d([[1, 2], [3]]))),
        ('which.mismatch', lambda: which(lambda: d([1, 2, 3], size=(2, 2)))),
        ('which.reshape', lambda: which(lambda: d([1, 2, 3]).reshape(2, 2))),
        ('which.setrow', lambda: which(lambda: d([[1, 2]]).__setitem__(0, [1]))),
        ('which.size', lambda: which(lambda: d(size=(-1, 2)))),
        ('which.over', lambda: which(lambda: i8([300]))),
        ('which.str', lambda: which(lambda: d(['a']))),
        ('which.index', lambda: which(lambda: d([[1, 2]])[5])),
    ]
    for name, fn in errors:
        run(name, fn)


# --- the package -------------------------------------------------------------------------------

def start():
    t0 = time.time()
    import k13pkg
    import matlab
    t1 = time.time()
    h = k13pkg.initialize()
    t2 = time.time()
    sys.stderr.write('TIMES\timport %.2f initialize %.2f\n' % (t1 - t0, t2 - t1))
    h.k9quiet(nargout=0)  # the beep off before the first error
    return k13pkg, matlab, h


class MyIO(io.StringIO):
    pass


def streams(fn):
    """A call given its own output and error StringIO: what each held, and the answer."""
    def go():
        o, e = io.StringIO(), io.StringIO()
        try:
            answer = desc(fn(o, e))
        except BaseException as x:
            answer = err(x)
        return Raw('%s ;; stdout=%r ;; stderr=%r' % (answer, o.getvalue(), e.getvalue()))
    return go


def mode_calls():
    pkg, matlab, h = start()
    say('package.type', '%s.%s' % (type(h).__module__, type(h).__name__))
    say('package.mro', str([k.__name__ for k in type(h).__mro__]))
    say('package.api', ' '.join(n for n in ('initialize', 'initialize_runtime', 'terminate_runtime') if hasattr(pkg, n)))
    say('handle.public', ' '.join(sorted(a for a in dir(h) if not a.startswith('_'))))
    f = h.k9echo
    say('function.type', '%s.%s' % (type(f).__module__, type(f).__name__))
    cases = [
        ('name', lambda: h.name),
        ('dunder', lambda: getattr(h, '__fred__')),
        ('function.sub', lambda: h.k9echo.inner._name),
        ('function.setattr', lambda: setattr(h.k9echo, 'x', 1)),
        ('function.repr', lambda: Raw(addr(repr(h.k9echo)))),
        ('call.float', lambda: h.k9echo(2.5)),
        ('call.int', lambda: h.k9echo(3)),
        ('call.default', lambda: h.k9multi(1.0)),
        ('nargout.0', lambda: h.k9multi(1.0, nargout=0)),
        ('nargout.1', lambda: h.k9multi(1.0, nargout=1)),
        ('nargout.2', lambda: h.k9multi(1.0, nargout=2)),
        ('nargout.3', lambda: h.k9multi(1.0, nargout=3)),
        ('nargout.4', lambda: h.k9multi(1.0, nargout=4)),
        ('nargout.true', lambda: h.k9multi(1.0, nargout=True)),
        ('nargout.float', lambda: h.k9multi(1.0, nargout=1.0)),
        ('nargout.negative', lambda: h.k9multi(1.0, nargout=-1)),
        ('nargout.str', lambda: h.k9multi(1.0, nargout='1')),
        ('nargout.None', lambda: h.k9multi(1.0, nargout=None)),
        ('nargout.np', np(lambda: h.k9multi(1.0, nargout=numpy.int64(2)))),
        ('noout.default', streams(lambda o, e: h.k9noout(1.0, stdout=o, stderr=e))),
        ('noout.0', streams(lambda o, e: h.k9noout(1.0, nargout=0, stdout=o, stderr=e))),
        ('noout.1', streams(lambda o, e: h.k9noout(1.0, nargout=1, stdout=o, stderr=e))),
        ('varargout.3', lambda: h.k9nout(2.0, nargout=3)),
        ('varargout.0', lambda: h.k9nout(2.0, nargout=0)),
        ('varargout.default', lambda: h.k9nout(2.0)),
        ('varargin.none', lambda: h.k9nargs()),
        ('varargin.three', lambda: h.k9nargs(1.0, 'two', [3.0])),
        ('inputs.many', streams(lambda o, e: h.k9echo(1.0, 2.0, stdout=o, stderr=e))),
        ('inputs.none', streams(lambda o, e: h.k9echo(stdout=o, stderr=e))),
        ('outputs.many', streams(lambda o, e: h.k9echo(1.0, nargout=2, stdout=o, stderr=e))),
        ('kwargs.bogus', lambda: h.k9echo(1.0, bogus=1)),
        ('kwargs.two', lambda: h.k9echo(1.0, zeta=1, alpha=2)),
        ('kwargs.nv', lambda: h.k9nv(1.0, Scale=2.0)),
        ('nv.pairs', lambda: h.k9nv(1.0, 'Scale', 2.0, 'Label', 'x')),
        ('nv.prefix', lambda: h.k9nv(1.0, 'sc', 3.0)),
        ('nv.bad', streams(lambda o, e: h.k9nv(1.0, 'Nope', 3.0, stdout=o, stderr=e))),
        ('print.capture', streams(lambda o, e: h.k9print(2.0, stdout=o, stderr=e))),
        ('print.stdout', streams(lambda o, e: h.k9print(1.0, stdout=o))),
        ('print.stderr', streams(lambda o, e: h.k9print(1.0, stderr=e))),
        ('stdout.notio', lambda: h.k9print(1.0, stdout=sys.stdout)),
        ('stderr.notio', lambda: h.k9print(1.0, stderr='x')),
        ('stdout.subclass', streams(lambda o, e: h.k9print(1.0, stdout=MyIO(), stderr=e))),
        ('stdout.bytesio', lambda: h.k9print(1.0, stdout=io.BytesIO())),
        ('stdout.None', streams(lambda o, e: h.k9echo(1.0, stdout=None, stderr=e))),
        ('unknown', lambda: h.nosuchfunc(1.0)),
        ('unknown.builtin', lambda: h.sqrt(4.0)),
        ('unknown.private', lambda: h._k9private(1.0)),
        ('unknown.dotted', lambda: h.k9pkg.k9inner(1.0)),
        ('unknown.method', lambda: h.norm(1.0)),
        ('unknown.nargout', lambda: h.nosuchfunc(1.0, nargout=-1)),
    ]
    for name, fn in cases:
        run(name, fn)

    for kind in ('plain', 'id', 'nested', 'cause', 'builtin', 'undefined'):
        run('error.' + kind, streams(lambda o, e, kind=kind: h.k9err(kind, stdout=o, stderr=e)))
    for kind in ('plain', 'id'):
        run('error.uncaptured.' + kind, lambda kind=kind: h.k9err(kind))
    run('error.mro', lambda: (lambda x: Raw('%s %s' % (type(x).__module__, [k.__name__ for k in type(x).__mro__])))(
        caught_exception(lambda: h.k9err('id'))))
    run('error.args', lambda: caught_exception(lambda: h.k9err('id')).args)
    run('error.public', lambda: Raw(' '.join(sorted(a for a in dir(caught_exception(lambda: h.k9err('id'))) if not a.startswith('__')))))
    run('error.after', lambda: h.k9echo(5.0))

    # Objects: what a value object, a handle object, an enumeration and a function handle come back as.
    p = h.k9make('point')
    run('object.point', lambda: p)
    run('object.point.public', lambda: Raw(' '.join(sorted(a for a in dir(p) if not a.startswith('__')))))
    run('object.point.in', lambda: h.k9describe(p))
    run('object.point.echo', lambda: h.k9describe(h.k9echo(p)))
    run('object.point.method', lambda: h.norm(p))
    run('object.point.list', lambda: h.k9describe([p, 1.0]))
    run('object.point.dict', lambda: h.k9describe({'p': p}))
    run('object.points', lambda: h.k9make('points'))
    run('object.counter', lambda: h.k9make('counter'))
    cnt = h.k9make('counter')
    run('object.counter.in', lambda: h.k9describe(cnt))
    run('object.enum', lambda: h.k9make('enum'))
    run('object.enums', lambda: h.k9make('enums'))
    run('object.enum.in', lambda: h.k9describe(h.k9make('enum')))
    run('object.handle', lambda: h.k9make('handle'))
    run('object.handle.in', lambda: h.k9describe(h.k9make('handle')))
    run('object.after', lambda: h.k9echo(1.0))


def caught_exception(fn):
    try:
        fn()
    except Exception as e:
        return e
    return None


# --- conversions -------------------------------------------------------------------------------

KINDS = ['double', 'negzero', 'row', 'column', 'matrix', 'nd', 'nd4', 'trailing', 'empty', 'empty03', 'single', 'singles',
         'int8', 'int8s', 'uint8', 'uint8m', 'int16', 'uint16', 'int32', 'int32s', 'uint32', 'int64', 'uint64', 'complex',
         'complexrow', 'complexsingle', 'complexint8', 'logical', 'logicals', 'logicalm', 'char', 'char1', 'charmatrix',
         'emptychar', 'unicode', 'string', 'strings', 'stringm', 'stringmissing', 'stringsmissing', 'emptystring',
         'stringempty', 'cell', 'cellcol', 'cellm', 'cellstr', 'cellstrcol', 'nestedcell', 'emptycell', 'struct',
         'structnested', 'structarray', 'structm', 'emptystruct', 'nofields', 'sparse', 'sparselogical', 'sparsecomplex',
         'inf', 'nan', 'pi', 'enum', 'enums', 'point', 'points', 'counter', 'dict', 'dictnum', 'dictcell', 'dictempty',
         'dictconfigured', 'handle', 'datetime', 'table', 'map', 'categorical', 'duration']


def mode_convert():
    pkg, matlab, h = start()
    for kind in KINDS:
        run('out.' + kind, lambda kind=kind: h.k9make(kind))
    import datetime
    import decimal
    import fractions
    m = matlab
    ins = [
        ('float', lambda: 1.5), ('negzero', lambda: -0.0), ('nan', lambda: float('nan')), ('inf', lambda: float('inf')),
        ('int', lambda: 3), ('intneg', lambda: -7), ('int.2^53+1', lambda: 2 ** 53 + 1), ('int.2^62', lambda: 2 ** 62),
        ('int.max', lambda: 2 ** 63 - 1), ('int.over', lambda: 2 ** 63), ('int.min', lambda: -2 ** 63),
        ('int.under', lambda: -2 ** 63 - 1), ('int.huge', lambda: 2 ** 64),
        ('true', lambda: True), ('false', lambda: False),
        ('complex', lambda: 1 + 2j), ('complex.zero', lambda: 3 + 0j),
        ('str', lambda: 'hello'), ('str.empty', lambda: ''), ('str.unicode', lambda: 'λ€A'),
        ('str.astral', lambda: '\U0001F600'), ('str.nul', lambda: 'a\x00b'), ('str.newline', lambda: 'a\nb'),
        ('bytes', lambda: b'ab'), ('bytearray', lambda: bytearray(b'ab')), ('memoryview', lambda: memoryview(b'ab')),
        ('None', lambda: None),
        ('list', lambda: [1.0, 2.0]), ('list.ints', lambda: [1, 2]), ('list.mixed', lambda: [1.0, 'a', True]),
        ('list.empty', lambda: []), ('list.nested', lambda: [[1.0, 2.0], [3.0, 4.0]]), ('list.dicts', lambda: [{'a': 1.0}]),
        ('list.strs', lambda: ['one', 'two']), ('list.None', lambda: [None]),
        ('tuple', lambda: (1.0, 2.0)), ('tuple.empty', lambda: ()), ('tuple.one', lambda: (1.0,)),
        ('dict', lambda: {'a': 1.0, 'b': 'x'}), ('dict.empty', lambda: {}), ('dict.nested', lambda: {'s': {'t': 1.0}}),
        ('dict.intkey', lambda: {1: 2.0}), ('dict.badname', lambda: {'1a': 2.0}), ('dict.space', lambda: {'a b': 1.0}),
        ('dict.list', lambda: {'v': [1.0, 2.0]}), ('dict.long', lambda: {'x' * 70: 1.0}), ('dict.order', lambda: {'z': 1.0, 'a': 2.0}),
        ('dict.unicode', lambda: {'λ': 1.0}), ('dict.None', lambda: {'a': None}),
        ('set', lambda: {1.0}), ('frozenset', lambda: frozenset([1.0])), ('range', lambda: range(3)),
        ('lambda', lambda: (lambda: 0)), ('object', lambda: object()), ('type', lambda: int),
        ('decimal', lambda: decimal.Decimal('1.5')), ('fraction', lambda: fractions.Fraction(1, 2)),
        ('datetime', lambda: datetime.datetime(2026, 1, 2, 3, 4, 5)),
        ('m.double', lambda: m.double([[1.0, 2.0], [3.0, 4.0]])), ('m.double.flat', lambda: m.double([1.0, 2.0, 3.0])),
        ('m.double.scalar', lambda: m.double(5.0)), ('m.double.empty', lambda: m.double([])),
        ('m.double.03', lambda: m.double(size=(0, 3))), ('m.double.cube', lambda: m.double([[[1, 2], [3, 4]], [[5, 6], [7, 8]]])),
        ('m.double.view', lambda: m.double([[1, 2], [3, 4]])[1]),
        ('m.complex', lambda: m.double([1 + 2j, 3], is_complex=True)), ('m.complex.realvalues', lambda: m.double([1, 2], is_complex=True)),
        ('m.single', lambda: m.single([1.5])), ('m.int8', lambda: m.int8([-5, 0, 127])),
        ('m.uint8', lambda: m.uint8([[1, 2], [3, 4]])), ('m.int16', lambda: m.int16([-7])), ('m.uint16', lambda: m.uint16([7])),
        ('m.int32', lambda: m.int32([-100000])), ('m.uint32', lambda: m.uint32([4000000000])),
        ('m.int64', lambda: m.int64([-9007199254740993])), ('m.int64.small', lambda: m.int64([5])),
        ('m.uint64', lambda: m.uint64([18446744073709551615])), ('m.uint64.small', lambda: m.uint64([5])),
        ('m.int8.complex', lambda: m.int8([1 + 2j], is_complex=True)), ('m.single.complex', lambda: m.single([2 + 3j], is_complex=True)),
        ('m.logical', lambda: m.logical([True, False])), ('m.logical.matrix', lambda: m.logical([[True, False], [False, True]])),
        ('m.list', lambda: [m.double([1.0, 2.0]), 'x']), ('m.dict', lambda: {'m': m.int8([1, 2])}),
        ('np.float64', np(lambda: numpy.float64(1.5))), ('np.int32', np(lambda: numpy.int32(3))),
        ('np.bool', np(lambda: numpy.bool_(True))), ('np.array', np(lambda: numpy.array([[1.0, 2.0], [3.0, 4.0]]))),
        ('np.int8s', np(lambda: numpy.array([1, 2], dtype=numpy.int8))), ('np.bools', np(lambda: numpy.array([True, False]))),
        ('np.complex', np(lambda: numpy.array([1 + 2j]))), ('np.cube', np(lambda: numpy.arange(8.).reshape(2, 2, 2))),
        ('np.fortran', np(lambda: numpy.asfortranarray(numpy.arange(6.).reshape(2, 3)))),
        ('np.strs', np(lambda: numpy.array(['a', 'bc']))), ('np.empty', np(lambda: numpy.zeros((0, 3)))),
        ('array.d', lambda: array.array('d', [1.0, 2.0])), ('array.i', lambda: array.array('i', [1, 2])),
        ('array.b', lambda: array.array('b', [1, 2])),
    ]
    for name, make in ins:
        def go(make=make):
            v = make()
            return v if isinstance(v, Raw) else h.k9describe(v)
        run('in.' + name, go)
    echoes = [
        ('int', lambda: 3), ('int.2^62', lambda: 2 ** 62), ('true', lambda: True), ('complex', lambda: 1 + 2j),
        ('str', lambda: 'abc'), ('str.empty', lambda: ''), ('list', lambda: [1, 'a', [2.0]]), ('list.empty', lambda: []),
        ('tuple', lambda: (1.0, 2.0)), ('dict', lambda: {'b': 1.0, 'a': [2.0, 'x']}), ('dict.empty', lambda: {}),
        ('m.scalar', lambda: m.double(5.0)), ('m.int8.scalar', lambda: m.int8([7])),
        ('m.logical.scalar', lambda: m.logical([True])), ('m.flat', lambda: m.double([1.0, 2.0])),
        ('m.column', lambda: m.double([[1.0], [2.0]])), ('m.empty', lambda: m.double([])),
        ('m.complex.scalar', lambda: m.double([1 + 2j], is_complex=True)), ('m.cube', lambda: m.double([[[1, 2], [3, 4]], [[5, 6], [7, 8]]])),
        ('m.trailing', lambda: m.double(size=(2, 3, 1))), ('m.uint64', lambda: m.uint64([2 ** 64 - 1])),
        ('m.single.scalar', lambda: m.single([0.1])), ('np.array', np(lambda: numpy.array([[1.0, 2.0], [3.0, 4.0]]))),
        ('nan', lambda: float('nan')), ('str.unicode', lambda: 'λ€A'), ('str.astral', lambda: '\U0001F600'),
    ]
    for name, make in echoes:
        def go(make=make):
            v = make()
            return v if isinstance(v, Raw) else h.k9echo(v)
        run('echo.' + name, go)


# --- background calls --------------------------------------------------------------------------

def mode_background():
    pkg, matlab, h = start()
    f = h.k9slow(1.0, background=True)
    say('future.type', '%s.%s' % (type(f).__module__, type(f).__name__))
    say('future.public', ' '.join(sorted(a for a in dir(f) if not a.startswith('__'))))
    run('future.done.early', lambda: f.done())
    run('future.result', lambda: f.result())
    run('future.done.late', lambda: f.done())
    run('future.cancelled', lambda: f.cancelled())
    run('future.result.again', lambda: f.result())
    run('future.cancel.done', lambda: f.cancel())
    run('future.cancelled.after', lambda: f.cancelled())

    g = h.k9slow(3.0, background=True)
    time.sleep(0.8)
    run('running.cancel', lambda: g.cancel())
    run('running.cancelled', lambda: g.cancelled())
    t = time.time()
    run('running.result', lambda: g.result())
    run('running.waited', lambda: round(time.time() - t))
    run('running.done', lambda: g.done())
    run('running.cancelled.after', lambda: g.cancelled())
    run('running.after', lambda: h.k9echo(1.0))

    first = h.k9slow(2.0, background=True)
    queued = h.k9echo(5.0, background=True)
    run('queued.cancel', lambda: queued.cancel())
    run('queued.cancelled', lambda: queued.cancelled())
    run('queued.done', lambda: queued.done())
    run('queued.result', lambda: queued.result())
    run('queued.first', lambda: first.result())

    s = h.k9slow(2.0, background=True)
    t = time.time()
    run('timeout.short', lambda: caught(lambda: s.result(timeout=0.3)))
    run('timeout.short.waited', lambda: round(time.time() - t, 1) >= 0.3)
    run('timeout.zero', lambda: caught(lambda: s.result(timeout=0)))
    run('timeout.negative', lambda: s.result(timeout=-1))
    run('timeout.str', lambda: s.result(timeout='x'))
    run('timeout.bool', lambda: caught(lambda: s.result(timeout=False)))
    run('timeout.long', lambda: s.result(timeout=30))

    o = io.StringIO()
    p = h.k9print(2.0, stdout=o, background=True)
    while not p.done():
        time.sleep(0.05)
    run('stdout.before', lambda: o.getvalue())
    run('stdout.result', lambda: p.result())
    run('stdout.after', lambda: o.getvalue())

    e = h.k9err('id', background=True)
    run('error.result', lambda: e.result())
    run('error.done', lambda: e.done())
    run('error.again', lambda: e.result())
    run('nargout', lambda: h.k9multi(2.0, nargout=3, background=True).result())
    run('nargout.0', lambda: h.k9multi(2.0, nargout=0, background=True).result())
    run('nargout.wrong', lambda: h.k9multi(2.0, nargout=4, background=True).result())
    run('noout.default', lambda: h.k9noout(2.0, background=True).result())
    run('async', lambda: (lambda x: (tname(x), x.result()))(h.k9echo(1.0, **{'async': True})))
    run('async.both', lambda: h.k9echo(1.0, **{'async': True, 'background': True}))
    run('async.notbool', lambda: h.k9echo(1.0, **{'async': 1}))
    run('background.notbool', lambda: h.k9echo(1.0, background=1))
    run('background.false', lambda: h.k9echo(1.0, background=False))
    run('unknown', lambda: h.nosuchfunc(1.0, background=True))
    many = [h.k9echo(float(k), background=True) for k in range(5)]
    run('order', lambda: [x.result() for x in many])
    run('forget.unfinished', lambda: (lambda x: None)(h.k9slow(0.5, background=True)))
    run('after', lambda: h.k9echo(2.0))


# --- a package's lifetime ----------------------------------------------------------------------

def mode_life():
    pkg, matlab, h = start()
    run('state.1', lambda: h.k13state())
    run('state.2', lambda: h.k13state())
    h2 = pkg.initialize()
    h2.k9quiet(nargout=0)
    run('second.state', lambda: h2.k13state())
    run('second.same', lambda: h2 is h)
    run('first.state', lambda: h.k13state())
    run('figures.none', lambda: h.wait_for_figures_to_close())
    run('terminate', lambda: h.terminate())
    run('after.terminate', lambda: h.k9echo(1.0))
    run('after.terminate.attr', lambda: h.k9echo)
    run('after.terminate.name', lambda: h.name)
    run('terminate.again', lambda: h.terminate())
    run('figures.after.terminate', lambda: h.wait_for_figures_to_close())
    run('second.after', lambda: h2.k13state())
    run('exit', lambda: h2.exit())
    run('after.exit', lambda: h2.k9echo(1.0))
    h3 = pkg.initialize()
    run('third.state', lambda: h3.k13state())
    run('quit', lambda: h3.quit())
    run('after.quit', lambda: h3.k9echo(1.0))
    h4 = pkg.initialize()
    run('fourth.echo', lambda: h4.k9echo(4.0))
    run('runtime.after.initialize', lambda: pkg.initialize_runtime([]))
    run('runtime.after.initialize.nojvm', lambda: pkg.initialize_runtime(['-nojvm']))
    run('fourth.after.runtime', lambda: h4.k9echo(5.0))
    del h4
    run('del', lambda: 'deleted')
    run('terminate_runtime', lambda: pkg.terminate_runtime())
    run('after.terminate_runtime', lambda: pkg.initialize())


def mode_runtime():
    import k13pkg
    which_case = ARGS[0]
    if which_case == 'nojvm':
        run('runtime.nojvm', lambda: k13pkg.initialize_runtime(['-nojvm']))
    elif which_case == 'str':
        run('runtime.str', lambda: k13pkg.initialize_runtime('-nojvm'))
    elif which_case == 'tuple':
        run('runtime.tuple', lambda: k13pkg.initialize_runtime(('-nojvm',)))
    elif which_case == 'bogus':
        run('runtime.bogus', lambda: k13pkg.initialize_runtime(['-bogus']))
    elif which_case == 'empty':
        run('runtime.empty', lambda: k13pkg.initialize_runtime([]))
    elif which_case == 'None':
        run('runtime.None', lambda: k13pkg.initialize_runtime(None))
    elif which_case == 'twice':
        run('runtime.twice.1', lambda: k13pkg.initialize_runtime(['-nojvm']))
        run('runtime.twice.2', lambda: k13pkg.initialize_runtime(['-nojvm']))
    elif which_case == 'number':
        run('runtime.number', lambda: k13pkg.initialize_runtime([1]))
    elif which_case == 'logfile':
        run('runtime.logfile', lambda: k13pkg.initialize_runtime(['-logfile', 'k13.log']))
    elif which_case == 'terminate':
        run('runtime.terminate.first', lambda: k13pkg.terminate_runtime())
    try:
        h = k13pkg.initialize()
        h.k9quiet(nargout=0)
        run('runtime.call', lambda: h.k9echo(1.0))
    except Exception as e:
        say('runtime.initialize', err(e))
    if which_case == 'logfile':
        import os
        run('runtime.logfile.exists', lambda: os.path.exists('k13.log'))


def mode_version():
    try:
        import k13pkg
        say('version.import', 'ok' + ('  WARN ' + ' / '.join(_caught) if _caught else ''))
        del _caught[:]
    except BaseException as e:
        say('version.import', err(e) + ('  WARN ' + ' / '.join(_caught) if _caught else ''))
        return
    sys.version_info = _real
    try:
        h = k13pkg.initialize()
        h.k9quiet(nargout=0)
        run('version.call', lambda: h.k9echo(1.0))
    except Exception as e:
        say('version.initialize', err(e))


def mode_extra():
    pkg, matlab, h = start()
    import datetime
    import matlab_pysdk.runtime as rt
    say('runtime.names', ' '.join(sorted(n for n in dir(rt) if not n.startswith('_') and isinstance(getattr(rt, n), type))))
    for n in ('MatlabRuntimeError', 'NumberOfOutputArgumentsError', 'InterruptedError', 'CancelledError', 'InitializationError',
              'MatlabExecutionError', 'RejectedExecutionError', 'TimeoutError', 'SyntaxError', 'DeployablePackage'):
        c = getattr(rt, n, None)
        say('runtime.class.' + n, 'missing' if c is None else '%s.%s mro=%s' % (c.__module__, c.__name__, [k.__name__ for k in c.__mro__]))
    say('matlab.classes', ' '.join(sorted(n for n in dir(matlab) if not n.startswith('_') and isinstance(getattr(matlab, n), type))))
    d = h.k9make('dict')
    cases = [
        ('dict.type', lambda: Raw('%s mro=%s' % (tname(d), [k.__name__ for k in type(d).__mro__]))),
        ('dict.public', lambda: Raw(' '.join(sorted(a for a in dir(d) if not a.startswith('_'))))),
        ('dict.len', lambda: len(d)),
        ('dict.keys', lambda: list(d.keys())),
        ('dict.values', lambda: list(d.values())),
        ('dict.items', lambda: list(d.items())),
        ('dict.get', lambda: d['a']),
        ('dict.get.missing', lambda: d['zz']),
        ('dict.in', lambda: 'a' in d),
        ('dict.iter', lambda: list(d)),
        ('dict.eq', lambda: d == {'a': 1.0, 'b': 2.0}),
        ('dict.isdict', lambda: isinstance(d, dict)),
        ('dict.str', lambda: Raw(str(d))),
        ('dict.set', mut(lambda: h.k9make('dict'), lambda x: x.__setitem__('c', 3.0))),
        ('dict.back', lambda: h.k9describe(d)),
        ('dict.echo', lambda: h.k9echo(d)),
        ('dict.new', lambda: matlab.dictionary({'x': 1.0})),
        ('dict.new.in', lambda: h.k9describe(matlab.dictionary({'x': 1.0}))),
        ('dict.new.empty', lambda: matlab.dictionary()),
        ('dict.num.keys', lambda: list(h.k9make('dictnum').keys())),
        ('dict.cell.values', lambda: list(h.k9make('dictcell').values())),
        ('object.size', lambda: h.k9make('points').size),
        ('object.size.one', lambda: h.k9make('point').size),
        ('object.size.handle', lambda: h.k9make('handle').size),
        ('object.size.table', lambda: h.k9make('table').size),
        ('object.new', lambda: matlab.object()),
        ('object.points.in', lambda: h.k9describe(h.k9make('points'))),
        ('object.table.in', lambda: h.k9describe(h.k9make('table'))),
        ('object.map.in', lambda: h.k9describe(h.k9make('map'))),
        ('object.categorical.in', lambda: h.k9describe(h.k9make('categorical'))),
        ('object.setattr', mut(lambda: h.k9make('point'), lambda x: setattr(x, 'X', 1))),
        ('object.getattr', lambda: h.k9make('point').X),
        ('object.eq', lambda: (lambda q: q == q)(h.k9make('point'))),
        ('object.list.in', lambda: h.k9describe([h.k9make('point'), h.k9make('counter')])),
        ('dt.tz.in', lambda: h.k9describe(datetime.datetime(2026, 1, 2, 3, 4, 5, tzinfo=datetime.timezone.utc))),
        ('dt.offset.in', lambda: h.k9describe(datetime.datetime(2026, 1, 2, 3, 4, 5, tzinfo=datetime.timezone(datetime.timedelta(hours=2))))),
        ('dt.micro.in', lambda: h.k9describe(datetime.datetime(2026, 1, 2, 3, 4, 5, 123456))),
        ('dt.date.in', lambda: h.k9describe(datetime.date(2026, 1, 2))),
        ('dt.time.in', lambda: h.k9describe(datetime.time(3, 4, 5))),
        ('dt.delta.in', lambda: h.k9describe(datetime.timedelta(days=1, seconds=2, microseconds=3))),
        ('dt.echo', lambda: h.k9echo(datetime.datetime(2026, 1, 2, 3, 4, 5, 123456))),
        ('dt.tz.echo', lambda: h.k9echo(datetime.datetime(2026, 1, 2, 3, 4, 5, tzinfo=datetime.timezone.utc))),
        ('dt.delta.echo', lambda: h.k9echo(datetime.timedelta(days=1, seconds=2, microseconds=3))),
        ('dt.list.in', lambda: h.k9describe([datetime.datetime(2026, 1, 2)])),
        ('in.dict.tuplekey', lambda: h.k9describe({('a',): 1.0})),
        ('in.list.matlabs', lambda: h.k9describe([matlab.double([[1, 2]]), matlab.int8([3])])),
        ('in.nested.tuple', lambda: h.k9describe(((1.0, 2.0), (3.0,)))),
        ('in.bools', lambda: h.k9describe([True, False])),
        ('in.np.uint64', np(lambda: h.k9describe(numpy.uint64(5)))),
        ('in.np.float32', np(lambda: h.k9describe(numpy.float32(1.5)))),
        ('in.np.complex64', np(lambda: h.k9describe(numpy.complex64(1 + 2j)))),
        ('in.np.2d.int', np(lambda: h.k9describe(numpy.array([[1, 2], [3, 4]])))),
        ('in.np.0d', np(lambda: h.k9describe(numpy.array(5.0)))),
        ('in.np.strided', np(lambda: h.k9describe(numpy.arange(6.)[::2]))),
        ('in.np.object', np(lambda: h.k9describe(numpy.array([1, 'a'], dtype=object)))),
        ('in.bytes.empty', lambda: h.k9describe(b'')),
        ('in.set.two', lambda: h.k9describe({1.0, 2.0})),
        ('in.range.list', lambda: h.k9describe([range(2)])),
        ('in.many', lambda: h.k9nargs(1.0, 'a', [1.0], {'k': 2.0})),
        ('in.matlab.view.int', lambda: h.k9describe(matlab.int8([[1, 2], [3, 4]])[0])),
        ('in.matlab.scalar.view', lambda: h.k9describe(matlab.double(5.0)[0])),
        ('in.empty.row', lambda: h.k9describe(matlab.double([[]]))),
        ('in.logical.scalar', lambda: h.k9describe(matlab.logical([True]))),
        ('error.str', lambda: Raw(repr(str(caught_exception(lambda: h.k9err('id')))))),
        ('error.repr', lambda: Raw(addr(repr(caught_exception(lambda: h.k9err('id')))))),
        ('out.after', lambda: h.k9echo(1.0)),
    ]
    for name, fn in cases:
        run(name, fn)


MODES = {'arrays': mode_arrays, 'calls': mode_calls, 'convert': mode_convert, 'background': mode_background,
         'life': mode_life, 'runtime': mode_runtime, 'version': mode_version, 'extra': mode_extra}
MODES[MODE]()
say('end', MODE)
