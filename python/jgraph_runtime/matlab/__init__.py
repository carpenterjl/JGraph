"""
The JGraph Runtime's matlab module (compiler and SDK plan, stage K13, decision KO3): MATLAB's
numeric and logical arrays in Python, as a deployed package's functions take and give them, under
the names R2025b's matlab module gives them so that client code runs unchanged. It lives only in
the runtime's python folder, as R2025b's lives in its runtime, and it is pure Python.

Each class behaves as record-k13.ps1 measured R2025b's (recordings\\k13_client.out.txt, arrays):
  - an array keeps its elements in column-major order, a complex array's parts interleaved;
  - a flat initializer is a row, a nested one gives a dimension per level, the outermost first,
    and size= reads a flat one in column-major order; trailing singleton dimensions are dropped;
  - indexing is 0-based and by the first dimension: a[i] is a view of one dimension less that
    shares the elements, a slice keeps the dimension, and a one-dimensional view gives numbers;
  - an element is converted as it goes in: an integer class refuses floating-point values and
    values out of its range, a complex value needs is_complex=True, and logical keeps truth;
  - the buffer is read-only and column-major. From Python 3.12, memoryview() and numpy read it
    in place; before 3.12 a Python class cannot export a buffer, so numpy reads it through
    __array_interface__ and tomemoryview() gives a row-major copy (ADR 0269).
"""

import array as _array
import builtins as _builtins
import ctypes as _ctypes
import numbers as _numbers
import struct as _struct
import sys as _sys

from mlexceptions import ShapeError, SizeError

__all__ = ['double', 'single', 'int8', 'uint8', 'int16', 'uint16', 'int32', 'uint32', 'int64', 'uint64', 'logical',
           'ShapeError', 'SizeError']

_LONG_LONG = 1 << 63
_HAS_BUFFER_PROTOCOL = _sys.version_info >= (3, 12)

# Each class: its array.array code, its buffer format, its element's size, its kind (f floating
# point, i signed integer, u unsigned integer, b logical) and an integer class's range.
_CLASSES = (
    ('double', 'd', 'd', 8, 'f', None),
    ('single', 'f', 'f', 4, 'f', None),
    ('int8', 'b', 'b', 1, 'i', (-(1 << 7), (1 << 7) - 1)),
    ('uint8', 'B', 'B', 1, 'u', (0, (1 << 8) - 1)),
    ('int16', 'h', 'h', 2, 'i', (-(1 << 15), (1 << 15) - 1)),
    ('uint16', 'H', 'H', 2, 'u', (0, (1 << 16) - 1)),
    ('int32', 'i', 'i', 4, 'i', (-(1 << 31), (1 << 31) - 1)),
    ('uint32', 'I', 'I', 4, 'u', (0, (1 << 32) - 1)),
    ('int64', 'q', 'q', 8, 'i', (-(1 << 63), (1 << 63) - 1)),
    ('uint64', 'Q', 'Q', 8, 'u', (0, (1 << 64) - 1)),
    ('logical', 'B', '?', 1, 'b', None),
)

# numpy's typestr for __array_interface__, by buffer format.
_TYPESTR = {'d': '<f8', 'f': '<f4', 'b': '|i1', 'B': '|u1', 'h': '<i2', 'H': '<u2', 'i': '<i4', 'I': '<u4', 'q': '<i8', 'Q': '<u8',
            '?': '|b1', 'Zd': '<c16', 'Zf': '<c8'}


class _Info(_builtins.object):
    """What a class is: its name, codes, element size, kind and range."""
    __slots__ = ('name', 'code', 'format', 'size', 'kind', 'range')

    def __init__(self, name, code, fmt, size, kind, limits):
        self.name, self.code, self.format, self.size, self.kind, self.range = name, code, fmt, size, kind, limits


class _State(_builtins.object):
    """An array's elements and where it lies in them: a view shares its parent's store."""
    __slots__ = ('store', 'complex', 'dims', 'offset', 'steps', 'view', 'root', 'pins')

    def __init__(self, store, cplx, dims, offset=0, steps=None, view=False, root=None):
        self.store = store
        self.complex = cplx
        self.dims = dims
        self.offset = offset
        self.steps = steps if steps is not None else _steps_of(dims)
        self.view = view
        self.root = root if root is not None else dims
        self.pins = []  # copies a view's buffers lie in, kept while the view lives


def _steps_of(dims):
    steps, n = [], 1
    for d in dims:
        steps.append(n)
        n *= d
    return tuple(steps)


def _count(dims):
    n = 1
    for d in dims:
        n *= d
    return n


def _trim(dims):
    """Dimensions as MATLAB keeps them: two at least, trailing singletons dropped."""
    dims = list(dims)
    while len(dims) > 2 and dims[-1] == 1:
        dims.pop()
    if len(dims) == 1:
        dims.insert(0, 1)
    return tuple(dims)


def _is_matlab(x):
    return type(x) in _CLASS_SET


def _is_sequence(x):
    if isinstance(x, (list, tuple, range)) or _is_matlab(x):
        return True
    if isinstance(x, (str, bytes, bytearray, dict, set, frozenset)):
        return False
    return hasattr(x, '__len__') and hasattr(x, '__getitem__') and not hasattr(x, 'keys')


def _is_number(x):
    if isinstance(x, _numbers.Number):
        return True
    # numpy's bool_ is not registered as a number.
    return type(x).__module__ == 'numpy' and type(x).__name__ in ('bool_', 'bool')


def _info(x):
    return type(x).__jgclass__


def _state(x):
    return _builtins.object.__getattribute__(x, '__jgraph__')


def _new(cls, state):
    made = _builtins.object.__new__(cls)
    _builtins.object.__setattr__(made, '__jgraph__', state)
    return made


def _store(info, cplx, n):
    """A zeroed store for n elements."""
    return _array.array(info.code, bytes(n * info.size * (2 if cplx else 1)))


def _type_name(x):
    """A type as R2025b's messages name it: the module before a matlab array's class."""
    return 'matlab.' + type(x).__name__ if _is_matlab(x) else type(x).__name__


# --- Converting an element ---------------------------------------------------------------------

def _integer(info, value):
    """An integer class's element, its range checked as R2025b checks it."""
    if info.name == 'uint64':
        if value < 0:
            raise OverflowError('value (%d) cannot be stored in a variable of type uint64.' % value)
        if value >= 1 << 64:
            raise OverflowError('Python int too large to convert to C long long')
        return value
    if not -_LONG_LONG <= value < _LONG_LONG:
        raise OverflowError('Python int too large to convert to C long long')
    low, high = info.range
    if not low <= value <= high:
        raise OverflowError('value (%d) cannot be stored in a variable of type %s.' % (value, info.name))
    return value


def _element(info, cplx, x, assigning):
    """One element as the class keeps it: a number, or for a complex array its two parts."""
    if x is None:
        raise TypeError("value must not be 'None'.")
    if not _is_number(x):
        if assigning:
            raise TypeError('value is not numeric.')
        if isinstance(x, (str, bytes)):
            raise ValueError('value passed to initializer must be numeric.')
        raise TypeError('type does not match.')
    kind = info.kind
    # An integer, a bool, and numpy's bool_ (a number that is not registered as one).
    if isinstance(x, (bool, _numbers.Integral)) or not isinstance(x, _numbers.Number):
        whole = int(x)
        if kind == 'f':
            if not -_LONG_LONG <= whole < _LONG_LONG:
                raise OverflowError('Python int too large to convert to C long long')
            value = float(whole)
        elif kind == 'b':
            value = 1 if whole else 0
        else:
            value = _integer(info, whole)
        return (value, 0) if cplx else value
    if isinstance(x, _numbers.Real):
        if kind == 'f':
            value = float(x)
        elif kind == 'b':
            value = 1 if x != 0 else 0
        else:
            raise TypeError('matlab.%s does not accept floating-point values.' % info.name)
        return (value, 0) if cplx else value
    z = complex(x)
    if not cplx:
        raise TypeError("matlab.%s does not accept complex values if 'is_complex' is not set to 'True'." % info.name)
    if kind == 'f':
        return (z.real, z.imag)
    if z.real != int(z.real) or z.imag != int(z.imag):
        raise TypeError('matlab.%s does not accept floating-point values.' % info.name)
    return (_integer(info, int(z.real)), _integer(info, int(z.imag)))


def _put(st, k, value):
    if st.complex:
        st.store[2 * k] = value[0]
        st.store[2 * k + 1] = value[1]
    else:
        st.store[k] = value


def _get(info, st, k):
    if st.complex:
        return complex(st.store[2 * k], st.store[2 * k + 1])
    value = st.store[k]
    return bool(value) if info.kind == 'b' else value


def _raw(st, k):
    """An element as its store keeps it, for a copy into a store of the same class."""
    return (st.store[2 * k], st.store[2 * k + 1]) if st.complex else st.store[k]


# --- Reading an initializer --------------------------------------------------------------------

def _category(x):
    if x is None:
        return 'none'
    if isinstance(x, (str, bytes)):
        return 'text'
    if _is_number(x):
        return 'number'
    if _is_sequence(x):
        return 'sequence'
    return 'other'


def _shape(seq):
    """A nested sequence's dimensions, the outermost first, each level checked as R2025b checks it."""
    items = list(seq)
    if not items:
        return (0,)
    kinds = [_category(x) for x in items]
    if 'none' in kinds:
        raise TypeError("value must not be 'None'.")
    first = kinds[0]
    if any(k != first for k in kinds):
        raise ValueError('elements of array are of different lengths or types.')
    if first == 'sequence':
        inner = [_shape(x) for x in items]
        if any(s != inner[0] for s in inner):
            raise ValueError('elements of array are of different lengths or types.')
        return (len(items),) + inner[0]
    if first == 'number':
        return (len(items),)
    if first == 'text':
        raise ValueError('value passed to initializer must be numeric.')
    raise TypeError('type does not match.')


def _nested_places(dims, steps, offset):
    """Where each element of a nested sequence of these dimensions goes, in the sequence's own order."""
    places = [offset]
    for d, s in zip(dims, steps):
        places = [p + j * s for p in places for j in range(d)]
    return places


def _flattened(seq, depth):
    """A nested sequence's elements, in its own order."""
    if depth == 1:
        return list(seq)
    found = []
    for x in seq:
        found.extend(_flattened(x, depth - 1))
    return found


def _format_matches(given, wanted):
    """Whether a buffer's element format is the class's: the same letter, or an integer of the same size and sign."""
    if given and given[0] in '@=<':
        given = given[1:]
    elif given and given[0] in '>!':
        return False
    if given == wanted:
        return True
    if given.startswith('Z') != wanted.startswith('Z'):
        return False
    g, w = given.lstrip('Z'), wanted.lstrip('Z')
    integers = 'bBhHiIlLqQnN'
    if len(g) == 1 and len(w) == 1 and g in integers and w in integers:
        return _struct.calcsize(g) == _struct.calcsize(w) and g.islower() == w.islower()
    return False


def _from_buffer(info, cplx, source):
    """An exporter's elements, its format the class's; a non-contiguous one refused as R2025b refuses it."""
    view = memoryview(source)
    wanted = ('Z' + info.format) if cplx else info.format
    if not _format_matches(view.format, wanted):
        raise BufferError("input buffer format ('%s') is not compatible with output format ('%s'); use a different input or output type"
                          % (view.format, wanted))
    if not (view.c_contiguous or view.f_contiguous):
        raise ValueError('%s is not contiguous' % type(source).__name__)
    shape = view.shape if view.ndim > 0 else (1,)
    store = _array.array(info.code)
    store.frombytes(view.tobytes(order='F'))
    if info.kind == 'b':
        store = _array.array('B', [1 if b else 0 for b in store])
    return shape, store


def _size_argument(size):
    if not _is_sequence(size) and not isinstance(size, str):
        raise TypeError("value of 'size' must be a sequence.")
    items = list(size)
    if not items:
        raise ValueError("sequence passed to 'size' must not be empty.")
    for d in items:
        if not isinstance(d, int):
            raise TypeError('dimensions must be nonnegative integers.')
        if d < 0:
            raise ValueError('size cannot contain negative dimensions. Use nonnegative integers for dimensions.')
    return _trim(tuple(int(d) for d in items))


def _sized(natural, flat, size):
    """The array's dimensions: an initializer's own, or size='s when its elements fit as R2025b allows."""
    if size is None:
        if flat:
            return (0, 0) if natural == (0,) else (1, natural[0])
        return _trim(natural)
    if flat:
        if natural[0] != _count(size):
            raise ShapeError("dimensions of initializer (%d) do not match dimensions specified by 'size' %r." % (natural[0], size))
        return size
    if _trim(natural) != size:
        raise SizeError("dimensions of initializer %r do not match dimensions specified by 'size' %r." % (tuple(natural), size))
    return size


_KEYWORDS = ('initializer', 'size', 'is_complex', 'vector')


def _construct(self, *args, **kwargs):
    info = _info(self)
    if len(args) > 3:
        raise TypeError('function takes at most 3 positional arguments (%d given)' % len(args))
    given = dict(zip(('initializer', 'size', 'vector'), args))
    for name, value in kwargs.items():
        if name not in _KEYWORDS:
            raise TypeError("'%s' is an invalid keyword argument for this function" % name)
        if name in given:
            raise TypeError("argument for function given by name ('%s') and position" % name)
        given[name] = value
    initializer = given.get('initializer')
    vector = given.get('vector')
    if 'vector' in given and initializer is not None:
        raise TypeError("more than two positional arguments must not be specified. A third will be treated as 'vector', "
                        "which must not be specified when an initializer is specified.")
    cplx = given.get('is_complex', False)
    if not isinstance(cplx, bool):
        raise TypeError("value passed to 'is_complex' must be a bool.")
    if cplx and info.kind == 'b':
        raise TypeError('logical arrays must not be complex.')
    size = _size_argument(given['size']) if given.get('size') is not None else None
    if initializer is None and vector is not None:
        initializer = vector
    _builtins.object.__setattr__(self, '__jgraph__', _read(info, cplx, initializer, size))


def _read(info, cplx, initializer, size):
    """The state an initializer and size give, every check in R2025b's order."""
    if initializer is None:
        dims = size if size is not None else (0, 0)
        return _State(_store(info, cplx, _count(dims)), cplx, dims)
    if _is_matlab(initializer):
        # Another array is read as the nested sequence it is: its elements converted one by one.
        source = _state(initializer)
        dims = _sized(source.dims, len(source.dims) == 1, size)
        st = _State(_store(info, cplx, _count(dims)), cplx, dims)
        for n, v in enumerate(_values(initializer)):
            _put(st, n, _element(info, cplx, v, False))
        return st
    if isinstance(initializer, str):
        raise ValueError('value passed to initializer must be numeric.')
    if _is_number(initializer):
        st = _State(_store(info, cplx, 1), cplx, _sized((1,), True, size))
        _put(st, 0, _element(info, cplx, initializer, False))
        return st
    try:
        memoryview(initializer)
        exported = True
    except TypeError:
        exported = False
    if exported:
        shape, store = _from_buffer(info, cplx, initializer)
        return _State(store, cplx, _sized(shape, len(shape) == 1, size))
    if not _is_sequence(initializer):
        raise TypeError('type does not match.')
    shape = _shape(initializer)
    dims = _sized(shape, len(shape) == 1, size)
    st = _State(_store(info, cplx, _count(dims)), cplx, dims)
    elements = _flattened(initializer, len(shape)) if _count(shape) else []
    places = range(len(elements)) if len(shape) == 1 else _nested_places(shape, _steps_of(shape), 0)
    for k, x in zip(places, elements):
        _put(st, k, _element(info, cplx, x, False))
    return st


# --- An array's elements -----------------------------------------------------------------------

def _indices(st):
    """The store index of each of the array's elements, in column-major order."""
    if not st.dims or 0 in st.dims:
        return []
    if not st.view:
        return range(st.offset, st.offset + _count(st.dims))
    found = [st.offset]
    for d, s in zip(st.dims, st.steps):
        found = [i + j * s for j in range(d) for i in found]
    return found


def _values(x):
    st = _state(x)
    info = _info(x)
    return [_get(info, st, k) for k in _indices(st)]


def _detached(x):
    """A copy of an array or view as an array of its own, a one-dimensional view a row."""
    st = _state(x)
    info = _info(x)
    dims = (1, st.dims[0]) if len(st.dims) == 1 else st.dims
    copy = _State(_store(info, st.complex, _count(dims)), st.complex, dims)
    for n, k in enumerate(_indices(st)):
        _put(copy, n, _raw(st, k))
    return _new(type(x), copy)


def _text(x):
    """The nested lists R2025b's repr and str write: elements by their own repr, no spaces."""
    st = _state(x)
    info = _info(x)
    last = len(st.dims) - 1

    def level(offset, at):
        d, s = st.dims[at], st.steps[at]
        if at == last:
            return '[' + ','.join(repr(_get(info, st, offset + j * s)) for j in range(d)) + ']'
        return '[' + ','.join(level(offset + j * s, at + 1) for j in range(d)) + ']'
    return level(st.offset, 0)


def _view(x, index):
    """a[index]: a view of one dimension less for a number, of the same for a slice; or, of a one-dimensional view, an element's place."""
    st = _state(x)
    n = st.dims[0]
    if isinstance(index, int):
        i = index + n if index < 0 else index
        if not 0 <= i < n:
            raise IndexError('index has an invalid value: %d' % index)
        offset = st.offset + i * st.steps[0]
        if len(st.dims) == 1:
            return None, offset
        return _State(st.store, st.complex, st.dims[1:], offset, st.steps[1:], True, st.root), None
    if isinstance(index, slice):
        start, stop, step = index.indices(n)
        count = len(range(start, stop, step))
        return _State(st.store, st.complex, (count,) + st.dims[1:], st.offset + start * st.steps[0],
                      (st.steps[0] * step,) + st.steps[1:], True, st.root), None
    raise RuntimeError('index is not an integer or slice.')


def _getitem(self, index):
    view, element = _view(self, index)
    if view is None:
        return _get(_info(self), _state(self), element)
    return _new(type(self), view)


def _setitem(self, index, value):
    info = _info(self)
    st = _state(self)
    view, element = _view(self, index)
    if view is None:
        _put(st, element, _element(info, st.complex, value, True))
        return
    # A view's elements are assigned whole: an array of the view's dimensions, or a nested
    # sequence of them; anything else, a number among them, is a different shape.
    if _is_matlab(value):
        if _state(value).dims != view.dims:
            raise ValueError('unable to perform assignment because the left and right sides have a different shape.')
        pairs = zip(_indices(view), _values(value))
    else:
        if not _is_sequence(value) or _shape(value) != view.dims:
            raise ValueError('unable to perform assignment because the left and right sides have a different shape.')
        pairs = zip(_nested_places(view.dims, view.steps, view.offset), _flattened(value, len(view.dims)))
    converted = [(k, _element(info, st.complex, v, True)) for k, v in pairs]
    for k, v in converted:
        _put(st, k, v)


def _delitem(self, index):
    # R2025b refuses a row this way and ends the process for an element; JGraph refuses both.
    raise RuntimeError('null data pointer.')


def _len(self):
    return _state(self).dims[0]


def _iter(self):
    return _MCPyIter(self)


def _eq(self, other):
    if type(other) is not type(self):
        return False if _is_matlab(other) else NotImplemented
    if _state(self).dims != _state(other).dims:
        return False
    return _values(self) == _values(other)


def _ne(self, other):
    same = _eq(self, other)
    return same if same is NotImplemented else not same


def _ordered(self, other):
    raise NotImplementedError('inequality operators not supported.')


def _operator(symbol, reflected):
    def refuse(self, other):
        left, right = (other, self) if reflected else (self, other)
        raise TypeError("unsupported operand type(s) for %s: '%s' and '%s'" % (symbol, _type_name(left), _type_name(right)))
    return refuse


_OPERATORS = {'add': '+', 'sub': '-', 'mul': '*', 'matmul': '@', 'truediv': '/', 'floordiv': '//', 'mod': '%', 'pow': '** or pow()',
              'and': '&', 'or': '|', 'xor': '^', 'lshift': '<<', 'rshift': '>>'}


def _repr(self):
    return 'matlab.%s(%s%s)' % (_info(self).name, _text(self), ', is_complex=True' if _state(self).complex else '')


def _str(self):
    return _text(self)


def _subclassed(cls, **kwargs):
    raise TypeError("type 'matlab.%s' is not an acceptable base type" % cls.__mro__[1].__name__)


_READ_ONLY = ('size', 'itemsize', '_data', '_real', '_imag', '_size', '_is_complex')


def _setattr(self, name, value):
    if name in _READ_ONLY:
        raise AttributeError("attribute '%s' of 'matlab.%s' objects is not writable" % (name, _info(self).name))
    raise AttributeError("'matlab.%s' object has no attribute '%s'" % (_info(self).name, name))


def _getattr(self, name):
    raise AttributeError("'matlab.%s' object has no attribute '%s'" % (_info(self).name, name))


def _reduce(self):
    st = _state(self)
    return (_rebuild, (_info(self).name, _values(self), (1, st.dims[0]) if len(st.dims) == 1 else st.dims, st.complex))


def _rebuild(name, values, dims, cplx):
    cls = _BY_NAME[name]
    info = cls.__jgclass__
    st = _State(_store(info, cplx, _count(dims)), cplx, tuple(dims))
    for k, v in enumerate(values):
        _put(st, k, (v.real, v.imag) if cplx else (1 if v else 0) if info.kind == 'b' else v)
    return _new(cls, st)


# --- The methods R2025b's arrays have ----------------------------------------------------------

def _size(self):
    return _state(self).dims


def _itemsize(self):
    return _info(self).size * (2 if _state(self).complex else 1)


def _is_complex_of(self):
    return _state(self).complex


def _one_dimensional(x, values):
    """A one-dimensional array of x's class holding values, an array of its own."""
    st = _State(_store(_info(x), False, len(values)), False, (len(values),))
    for k, v in enumerate(values):
        st.store[k] = v
    return _new(type(x), st)


def noncomplex(self):
    """noncomplex returns the the input object (which must be noncomplex) as a 1-by-N array"""
    st = _state(self)
    if st.complex:
        raise ValueError('noncomplex() must not be called on a complex array.')
    return _one_dimensional(self, [st.store[k] for k in _indices(st)])


def real(self):
    """real returns the real part of the input object as a 1-by-N array"""
    st = _state(self)
    if not st.complex:
        raise ValueError('real() and imag() can be called only on a complex array.')
    return _one_dimensional(self, [st.store[2 * k] for k in _indices(st)])


def imag(self):
    """imag returns the imaginary part of the input object as a 1-by-N array"""
    st = _state(self)
    if not st.complex:
        raise ValueError('real() and imag() can be called only on a complex array.')
    return _one_dimensional(self, [st.store[2 * k + 1] for k in _indices(st)])


def reshape(self, *dims):
    """reshape changes the dimensions of an N-dimensional array while preserving the values"""
    message = 'reshape() must be passed a nonempty sequence of nonnegative integers.'
    if len(dims) == 1:
        if not _is_sequence(dims[0]):
            raise ValueError(message)
        dims = tuple(dims[0])
    if not dims or any(not isinstance(d, int) or d < 0 for d in dims):
        raise ValueError(message)
    st = _state(self)
    if st.view:
        raise RuntimeError('reshape() not supported for a partial array.')
    new = _trim(tuple(int(d) for d in dims))
    if _count(new) != _count(st.dims):
        raise ShapeError('number of elements specified by arguments to reshape() (%d) does not match current number (%d).'
                         % (_count(new), _count(st.dims)))
    st.dims = new
    st.steps = _steps_of(new)
    st.root = new
    return self


def toarray(self):
    """toarray returns an array.array initialized from the input object"""
    st = _state(self)
    if len(st.dims) != 1:
        raise RuntimeError('toarray() can be called only on a one-dimensional sequence.')
    if st.complex:
        raise TypeError('toarray() cannot be called on a complex array: array.array keeps no complex numbers.')
    return _array.array(_info(self).code, [st.store[k] for k in _indices(st)])


def clone(self):
    """clone returns an independent copy of the input object"""
    return _detached(self)


def buffer_info(self):
    """buffer_info returns a two-element tuple containing the address of the buffer and the number of elements"""
    st = _state(self)
    return (st.store.buffer_info()[0], len(st.store) // (2 if st.complex else 1))


def get_bytewise_strides(self):
    """get_bytewise_strides returns a tuple containing the bytewise strides"""
    size = _itemsize(self)
    return tuple(s * size for s in _steps_of(_state(self).root))


# --- The buffer --------------------------------------------------------------------------------

class _PyBuffer(_ctypes.Structure):
    _fields_ = [('buf', _ctypes.c_void_p), ('obj', _ctypes.c_void_p), ('len', _ctypes.c_ssize_t), ('itemsize', _ctypes.c_ssize_t),
                ('readonly', _ctypes.c_int), ('ndim', _ctypes.c_int), ('format', _ctypes.c_void_p),
                ('shape', _ctypes.POINTER(_ctypes.c_ssize_t)), ('strides', _ctypes.POINTER(_ctypes.c_ssize_t)),
                ('suboffsets', _ctypes.POINTER(_ctypes.c_ssize_t)), ('internal', _ctypes.c_void_p)]


_from_py_buffer = _ctypes.pythonapi.PyMemoryView_FromBuffer
_from_py_buffer.argtypes = [_ctypes.POINTER(_PyBuffer)]
_from_py_buffer.restype = _ctypes.py_object
_EMPTY = (_ctypes.c_char * 16)()

# Formats as C text that outlives every memoryview, which keeps a pointer to its format.
_FORMATS = {}


def _format_address(fmt):
    held = _FORMATS.get(fmt)
    if held is None:
        held = _FORMATS[fmt] = _ctypes.create_string_buffer(fmt.encode('ascii'))
    return _ctypes.addressof(held)


def _whole(x):
    """The array a buffer lies in: x itself, or for a view a copy of it that the view keeps."""
    st = _state(x)
    if not st.view:
        return x
    copy = _detached(x)
    st.pins.append(copy)
    return copy


def _strided_view(x):
    """A read-only memoryview over an array's store, column-major as R2025b's buffer is. The
    memoryview holds no reference to the store: whoever asked for it through the buffer protocol
    holds the array (Python 3.12's buffer wrapper does), and the array holds the store."""
    st = _state(x)
    info = _info(x)
    size = _itemsize(x)
    fmt = ('Z' + info.format) if st.complex else info.format
    shape = (_ctypes.c_ssize_t * len(st.dims))(*st.dims)
    strides = (_ctypes.c_ssize_t * len(st.dims))(*(s * size for s in st.steps))
    view = _PyBuffer()
    view.buf = st.store.buffer_info()[0] if len(st.store) else _ctypes.addressof(_EMPTY)
    view.len = _count(st.dims) * size
    view.itemsize = size
    view.readonly = 1
    view.ndim = len(st.dims)
    view.format = _format_address(fmt)
    view.shape = shape
    view.strides = strides
    return _from_py_buffer(_ctypes.byref(view))


def _buffer(self, flags):
    return _strided_view(_whole(self))


def _release_buffer(self, view):
    pass


def tomemoryview(self):
    """tomemoryview returns a memoryview initialized from the input object"""
    if _HAS_BUFFER_PROTOCOL:
        return memoryview(self)
    # Before Python 3.12 a Python class cannot export a buffer: a row-major copy, read-only.
    whole = _whole(self)
    st = _state(whole)
    info = _info(whole)
    if st.complex:
        raise TypeError('tomemoryview() of a complex array needs Python 3.12 or later.')
    places = [] if 0 in st.dims else _nested_places(st.dims, st.steps, 0)
    data = _array.array(info.code, [st.store[k] for k in places]).tobytes()
    return memoryview(data).cast(info.format, st.dims)


def _array_interface(self):
    """numpy's way to the buffer before Python 3.12; numpy keeps the array, which keeps the store."""
    whole = _whole(self)
    st = _state(whole)
    info = _info(whole)
    size = _itemsize(whole)
    fmt = ('Z' + info.format) if st.complex else info.format
    address = st.store.buffer_info()[0] if len(st.store) else _ctypes.addressof(_EMPTY)
    return {'shape': st.dims, 'typestr': _TYPESTR[fmt], 'data': (address, True), 'strides': tuple(s * size for s in st.steps),
            'version': 3}


def _make_class(row):
    name, code, fmt, size, kind, limits = row
    namespace = {
        '__slots__': ('__jgraph__',),
        '__module__': 'matlab',
        '__qualname__': name,
        '__doc__': None,
        '__jgclass__': _Info(name, code, fmt, size, kind, limits),
        '__init__': _construct,
        '__init_subclass__': classmethod(_subclassed),
        '__getitem__': _getitem,
        '__setitem__': _setitem,
        '__delitem__': _delitem,
        '__len__': _len,
        '__iter__': _iter,
        '__eq__': _eq,
        '__ne__': _ne,
        '__lt__': _ordered,
        '__le__': _ordered,
        '__gt__': _ordered,
        '__ge__': _ordered,
        # Unhashable, as R2025b's are; Python's refusal then names the class without its module.
        '__hash__': None,
        '__repr__': _repr,
        '__str__': _str,
        '__setattr__': _setattr,
        '__getattr__': _getattr,
        '__reduce__': _reduce,
        '__buffer__': _buffer,
        '__release_buffer__': _release_buffer,
        'size': property(_size, doc='dimensions'),
        '_size': property(_size, doc='dimensions'),
        'itemsize': property(_itemsize, doc='size of an element in bytes'),
        '_is_complex': property(_is_complex_of, doc='whether array is complex'),
        '_data': property(noncomplex, doc='elements in storage order; equivalent to noncomplex()'),
        '_real': property(real, doc='real part of elements; equivalent to real()'),
        '_imag': property(imag, doc='imaginary part of elements; equivalent to imag()'),
        'noncomplex': noncomplex,
        'real': real,
        'imag': imag,
        'reshape': reshape,
        'toarray': toarray,
        'tomemoryview': tomemoryview,
        'clone': clone,
        'buffer_info': buffer_info,
        'get_bytewise_strides': get_bytewise_strides,
    }
    for word, symbol in _OPERATORS.items():
        namespace['__%s__' % word] = _operator(symbol, False)
        namespace['__r%s__' % word] = _operator(symbol, True)
    if not _HAS_BUFFER_PROTOCOL:
        namespace['__array_interface__'] = property(_array_interface)
    return type(name, (_builtins.object,), namespace)


double, single, int8, uint8, int16, uint16, int32, uint32, int64, uint64, logical = (_make_class(row) for row in _CLASSES)
_BY_NAME = {cls.__name__: cls for cls in (double, single, int8, uint8, int16, uint16, int32, uint32, int64, uint64, logical)}
_CLASS_SET = frozenset(_BY_NAME.values())


class _MCPyIter(_builtins.object):
    """An iterator over an array's first dimension, named as R2025b's is (k13_client: iter.type)."""
    __slots__ = ('__jgraph__',)

    def __init__(self, source):
        self.__jgraph__ = [source, 0]

    def __iter__(self):
        return self

    def __next__(self):
        source, at = self.__jgraph__
        if at >= len(source):
            raise StopIteration
        self.__jgraph__[1] = at + 1
        return source[at]


_MCPyIter.__module__ = 'matlab'
_MCPyIter.__name__ = _MCPyIter.__qualname__ = 'MCPyIter'


def _from_store(name, store, dims, cplx):
    """An array of the class name over the elements a call gave (matlab_pysdk.runtime's conversions)."""
    return _new(_BY_NAME[name], _State(store, cplx, tuple(dims)))


# --- MATLAB values Python keeps no data of -----------------------------------------------------

class _NothingHeld(_builtins.object):
    """What a matlab.object a program made itself holds: no MATLAB value."""
    __slots__ = ()
    dims = (0, 0)
    handle = None

    @staticmethod
    def release():
        pass


def _object_init(self):
    _builtins.object.__setattr__(self, '__jgraph__', _NothingHeld())


def _object_size(self):
    return _builtins.object.__getattribute__(self, '__jgraph__').dims


def _object_setattr(self, name, value):
    raise AttributeError("'matlab.object' object has no attribute '%s'" % name)


def _object_getattr(self, name):
    raise AttributeError("'matlab.object' object has no attribute '%s'" % name)


def _object_del(self):
    try:
        held = _builtins.object.__getattribute__(self, '__jgraph__')
    except AttributeError:
        return
    try:
        held.release()
    except Exception:  # an interpreter that is shutting down may have let the runtime go already
        pass


# An object, a function handle, a containers.Map, a table: the engine keeps the value for the
# package instance that returned it, and this holds the engine's handle on it (matlab_pysdk.runtime's
# HeldValue: its kind, class, dims and handle, and release(), which lets the engine's value go).
_MatlabObject = type('object', (_builtins.object,), {
    '__slots__': ('__jgraph__', '__weakref__'),
    '__module__': 'matlab',
    '__qualname__': 'object',
    '__doc__': None,
    '__init__': _object_init,
    '__del__': _object_del,
    '__setattr__': _object_setattr,
    '__getattr__': _object_getattr,
    'size': property(_object_size, doc='dimensions'),
})


def _held_object(held):
    """A matlab.object over what the runtime holds for it."""
    made = _builtins.object.__new__(_MatlabObject)
    _builtins.object.__setattr__(made, '__jgraph__', held)
    return made


class dictionary(_builtins.object):
    """A MATLAB dictionary in Python, as R2025b's runtime gives one (k13_client: out.dict, dict.*): its keys
    and values, in order, each converted; a dict of the same pairs is not equal to it but its todict() is."""
    __slots__ = ('__jgraph__',)

    def __init__(self, *args, **kwargs):
        _builtins.object.__setattr__(self, '__jgraph__', dict(*args, **kwargs))

    def __len__(self):
        return len(self.__jgraph__)

    def __getitem__(self, key):
        return self.__jgraph__[key]

    def __setitem__(self, key, value):
        self.__jgraph__[key] = value

    def __contains__(self, key):
        return key in self.__jgraph__

    def __iter__(self):
        return iter(list(self.__jgraph__))

    def __eq__(self, other):
        if type(other) is not dictionary:
            raise TypeError("comparison of objects of type 'matlab.dictionary' with objects of another type not supported")
        return self.__jgraph__ == other.__jgraph__

    def __ne__(self, other):
        return not self.__eq__(other)

    __hash__ = None

    def __repr__(self):
        return 'matlab.dictionary(%r)' % (self.__jgraph__,)

    def __str__(self):
        return str(self.__jgraph__)

    def __setattr__(self, name, value):
        raise AttributeError("'matlab.dictionary' object has no attribute '%s'" % name)

    def keys(self):
        return list(self.__jgraph__.keys())

    def values(self):
        return list(self.__jgraph__.values())

    def items(self):
        return list(self.__jgraph__.items())

    def clear(self):
        self.__jgraph__.clear()

    def todict(self):
        return dict(self.__jgraph__)


dictionary.__module__ = 'matlab'

globals()['object'] = _MatlabObject
