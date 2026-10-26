"""
Python values to MATLAB's and back (compiler and SDK plan, stage K13), in the binary form the
JGraph Runtime's calls take and give (JGraph.Runtime's NativeWire: each array its type as a byte,
its dimensions, then its elements in column-major order). The conversions are R2025b's, as
record-k13.ps1 measured its Python runtime making them (recordings\\k13_client.out.txt: convert,
extra): an int is int64, a float double, a bool logical, a str char, bytes uint8, a list, tuple or
set a cell row, a dict a struct, a datetime a datetime, a timedelta a duration; a matlab array keeps
its class and size. Back again a 1-by-1 number is a Python number, char text, a string row a list
of str, a cell row a list, a scalar struct a dict, an object a matlab.object.
"""

import array
import datetime
import re
import struct

import matlab

LOGICAL, CHAR, STRING = 0, 1, 2
NUMBERS = ('double', 'single', 'int8', 'uint8', 'int16', 'uint16', 'int32', 'uint32', 'int64', 'uint64')
FIRST_NUMBER, FIRST_COMPLEX = 3, 13
CELL, STRUCT, OBJECT, VALUE_OBJECT, HANDLE_OBJECT_REF, ENUM = 23, 24, 25, 26, 27, 28
SPARSE_LOGICAL, SPARSE_DOUBLE, SPARSE_COMPLEX_DOUBLE, SPARSE_SINGLE, SPARSE_COMPLEX_SINGLE = 29, 30, 31, 33, 34
PY_DATETIME, PY_DURATION, PY_DICTIONARY = 100, 101, 102

_CODES = {'double': 'd', 'single': 'f', 'int8': 'b', 'uint8': 'B', 'int16': 'h', 'uint16': 'H', 'int32': 'i', 'uint32': 'I',
          'int64': 'q', 'uint64': 'Q'}
_SIZES = {name: struct.calcsize(code) for name, code in _CODES.items()}

# A buffer's format, its byte order taken off, as the class it is (k13_client: in.np.*, in.array.*).
_BUFFER_CLASSES = {'d': 'double', 'f': 'single', 'b': 'int8', 'B': 'uint8', 'h': 'int16', 'H': 'uint16', '?': 'logical'}

_EPOCH = datetime.datetime(1899, 12, 30)
_MILLISECOND = datetime.timedelta(milliseconds=1)
_IDENTIFIER = re.compile(r'[A-Za-z][A-Za-z0-9_]*\Z')


def _type_name(value):
    t = type(value)
    return t.__name__ if t.__module__ == 'builtins' else '%s.%s' % (t.__module__, t.__name__)


def _unsupported(value):
    return TypeError("conversion of Python data type '%s' to MATLAB not supported" % _type_name(value))


class Writer(object):
    """The inputs of a call, as bytes."""

    def __init__(self):
        self.bytes = bytearray()

    def header(self, kind, dims):
        self.bytes += struct.pack('<BI', kind, len(dims))
        self.bytes += struct.pack('<%dQ' % len(dims), *dims)

    def u32(self, n):
        self.bytes += struct.pack('<I', n)

    def u64(self, n):
        self.bytes += struct.pack('<Q', n)

    def byte(self, n):
        self.bytes.append(n)

    def double(self, x):
        self.bytes += struct.pack('<d', x)

    def text8(self, s):
        data = s.encode('utf-8')
        self.u32(len(data))
        self.bytes += data

    def text16(self, s):
        data = s.encode('utf-16-le', 'surrogatepass')
        self.u32(len(data) // 2)
        self.bytes += data


def _is_numpy_bool(value):
    return type(value).__module__ == 'numpy' and type(value).__name__ in ('bool_', 'bool')


def encode(w, value):
    """Writes value as the MATLAB value R2025b makes of it, or refuses it in R2025b's words."""
    if value is None:
        raise TypeError("passing 'None' to MATLAB not supported")
    if isinstance(value, bool) or _is_numpy_bool(value):
        w.header(LOGICAL, (1, 1))
        w.byte(1 if value else 0)
        return
    if isinstance(value, int):
        if not -(1 << 63) <= value < (1 << 63):
            raise OverflowError('int too big to convert')
        w.header(FIRST_NUMBER + NUMBERS.index('int64'), (1, 1))
        w.bytes += struct.pack('<q', value)
        return
    if isinstance(value, float):
        w.header(FIRST_NUMBER, (1, 1))
        w.double(value)
        return
    if isinstance(value, complex):
        w.header(FIRST_COMPLEX, (1, 1))
        w.bytes += struct.pack('<dd', value.real, value.imag)
        return
    if isinstance(value, str):
        w.header(CHAR, (1, len(value.encode('utf-16-le', 'surrogatepass')) // 2) if value else (0, 0))
        w.bytes += value.encode('utf-16-le', 'surrogatepass')
        return
    if isinstance(value, (bytes, bytearray)):
        w.header(FIRST_NUMBER + NUMBERS.index('uint8'), (1, len(value)) if value else (0, 0))
        w.bytes += bytes(value)
        return
    if type(value) in matlab._CLASS_SET:
        _encode_array(w, value)
        return
    if isinstance(value, matlab.dictionary):
        _encode_dictionary(w, value)
        return
    if type(value) is matlab.object:
        _encode_object(w, value)
        return
    if isinstance(value, datetime.datetime):
        offset = value.utcoffset()
        w.header(PY_DATETIME, (1, 1))
        w.double((value.replace(tzinfo=None) - _EPOCH) / _MILLISECOND)
        w.byte(0 if offset is None else 1)
        w.double(0.0 if offset is None else offset.total_seconds())
        return
    if isinstance(value, datetime.timedelta):
        w.header(PY_DURATION, (1, 1))
        w.double(value / _MILLISECOND)
        return
    if isinstance(value, dict):
        _encode_struct(w, value)
        return
    # A range is a sequence as a list is, which R2025b's runtime takes for a dictionary and cannot read.
    if isinstance(value, (list, tuple, set, range)):
        items = list(value)
        w.header(CELL, (1, len(items)))
        for item in items:
            encode(w, item)
        return
    try:
        view = memoryview(value)
    except TypeError:
        raise _unsupported(value)
    _encode_buffer(w, view)


def _encode_array(w, value):
    st = matlab._state(value)
    info = matlab._info(value)
    dims = (1, st.dims[0]) if len(st.dims) == 1 else st.dims
    if info.kind == 'b':
        w.header(LOGICAL, dims)
    else:
        w.header((FIRST_COMPLEX if st.complex else FIRST_NUMBER) + NUMBERS.index(info.name), dims)
    if not st.view:
        w.bytes += st.store.tobytes()
        return
    width = 2 if st.complex else 1
    picked = array.array(st.store.typecode)
    for k in matlab._indices(st):
        picked.extend(st.store[width * k: width * k + width])
    w.bytes += picked.tobytes()


def _encode_buffer(w, view):
    fmt = view.format
    if fmt and fmt[0] in '@=<':
        fmt = fmt[1:]
    if not (view.c_contiguous or view.f_contiguous):
        raise ValueError('%s is not contiguous' % type(view.obj).__name__)
    name = _BUFFER_CLASSES.get(fmt)
    cplx = False
    if name is None and fmt in ('Zd', 'Zf'):
        name, cplx = ('double' if fmt == 'Zd' else 'single'), True
    if name is None and len(fmt) == 1 and fmt in 'iIlLqQnN':
        size = struct.calcsize(fmt)
        name = ('int' if fmt.islower() else 'uint') + str(8 * size)
    if name is None:
        raise RuntimeError('format character not supported: %s' % (fmt[:1] or '?'))
    shape = view.shape
    dims = (1, 1) if view.ndim == 0 else (1, shape[0]) if view.ndim == 1 else tuple(shape)
    if name == 'logical':
        w.header(LOGICAL, dims)
        w.bytes += bytes(1 if b else 0 for b in view.tobytes(order='F'))
        return
    w.header((FIRST_COMPLEX if cplx else FIRST_NUMBER) + NUMBERS.index(name), dims)
    w.bytes += view.tobytes(order='F')


def _encode_struct(w, value):
    names = list(value.keys())
    for name in names:
        if not isinstance(name, str) or not name:
            raise ValueError('field name of Python dict object passed to MATLAB must be a nonempty string')
        if not _IDENTIFIER.match(name):
            raise ValueError("field name is not a valid MATLAB identifier: '%s'" % name)
    w.header(STRUCT, (1, 1))
    w.u32(len(names))
    for name in names:
        w.text8(name)
    for name in names:
        encode(w, value[name])


def _typed_row(w, items):
    """A dictionary's keys or values as one array: strings, logicals, int64s or doubles when they all are, else a cell."""
    if items and all(isinstance(x, str) for x in items):
        w.header(STRING, (1, len(items)))
        for x in items:
            w.byte(1)
            w.text16(x)
    elif items and all(isinstance(x, bool) for x in items):
        w.header(LOGICAL, (1, len(items)))
        for x in items:
            w.byte(1 if x else 0)
    elif items and all(isinstance(x, int) and not isinstance(x, bool) for x in items):
        w.header(FIRST_NUMBER + NUMBERS.index('int64'), (1, len(items)))
        w.bytes += struct.pack('<%dq' % len(items), *items)
    elif items and all(isinstance(x, (int, float)) and not isinstance(x, bool) for x in items):
        w.header(FIRST_NUMBER, (1, len(items)))
        w.bytes += struct.pack('<%dd' % len(items), *(float(x) for x in items))
    else:
        w.header(CELL, (1, len(items)))
        for x in items:
            encode(w, x)


def _encode_dictionary(w, value):
    pairs = list(value.items())
    w.header(PY_DICTIONARY, (1, 1))
    w.byte(1 if pairs else 0)
    if pairs:
        _typed_row(w, [k for k, _ in pairs])
        _typed_row(w, [v for _, v in pairs])


def _encode_object(w, value):
    held = object.__getattribute__(value, '__jgraph__')
    if getattr(held, 'handle', None) is None:
        raise TypeError("the matlab.object holds no MATLAB value: only one a function returned can be passed to MATLAB")
    w.header(held.kind, held.dims)
    w.text8(held.class_name)
    for i in range(held.count):
        w.u64(held.handle)
        w.u64(i)


# --- Back from MATLAB --------------------------------------------------------------------------

class Reader(object):
    """A call's outputs, or an error's record, read in order."""

    def __init__(self, data):
        self.data = data
        self.at = 0

    def take(self, n):
        part = self.data[self.at:self.at + n]
        if len(part) != n:
            raise SystemError('Error in MATLAB Compiler SDK for Python. Details: the outputs of a call are damaged.')
        self.at += n
        return part

    def byte(self):
        return self.take(1)[0]

    def u32(self):
        return struct.unpack('<I', self.take(4))[0]

    def u64(self):
        return struct.unpack('<Q', self.take(8))[0]

    def double(self):
        return struct.unpack('<d', self.take(8))[0]

    def text8(self):
        return self.take(self.u32()).decode('utf-8')

    def text16(self):
        return self.take(2 * self.u32()).decode('utf-16-le', 'surrogatepass')

    @property
    def at_end(self):
        return self.at >= len(self.data)


def _count(dims):
    n = 1
    for d in dims:
        n *= d
    return n


def _is_vector(dims):
    return len(dims) == 2 and (dims[0] == 1 or dims[1] == 1)


def _is_scalar(dims):
    return all(d == 1 for d in dims)


class HeldValue(object):
    """What a matlab.object holds: the engine's handle on a value a call returned, let go of once."""
    __slots__ = ('kind', 'class_name', 'dims', 'handle', 'count', 'releaser')

    def __init__(self, kind, class_name, dims, handle, count, releaser):
        self.kind, self.class_name, self.dims, self.handle, self.count, self.releaser = kind, class_name, dims, handle, count, releaser

    def release(self):
        handle, self.handle = self.handle, None
        if handle is not None and self.releaser is not None:
            self.releaser(handle)


def decode(r, release):
    """The next value, as R2025b's runtime gives it to Python; release lets go of an object's handle."""
    kind = r.byte()
    dims = tuple(r.u64() for _ in range(r.u32()))
    n = _count(dims)
    if kind == LOGICAL:
        data = r.take(n)
        if _is_scalar(dims):
            return data[0] != 0
        return matlab._from_store('logical', array.array('B', [1 if b else 0 for b in data]), dims, False)
    if kind == CHAR:
        text = r.take(2 * n).decode('utf-16-le', 'surrogatepass')
        if n and not _is_vector(dims):
            raise ValueError('char arrays returned from MATLAB must be 1-by-N or M-by-1')
        return text
    if kind == STRING:
        items = [r.text16() if r.byte() else None for _ in range(n)]
        if _is_scalar(dims):
            return items[0]
        if n and not _is_vector(dims):
            raise ValueError('string arrays returned from MATLAB must be 1-by-N or M-by-1')
        return items
    if FIRST_NUMBER <= kind < FIRST_NUMBER + 10 or FIRST_COMPLEX <= kind < FIRST_COMPLEX + 10:
        cplx = kind >= FIRST_COMPLEX
        name = NUMBERS[kind - (FIRST_COMPLEX if cplx else FIRST_NUMBER)]
        store = array.array(_CODES[name])
        store.frombytes(r.take(n * _SIZES[name] * (2 if cplx else 1)))
        if _is_scalar(dims):
            return complex(store[0], store[1]) if cplx else store[0]
        return matlab._from_store(name, store, dims, cplx)
    if kind == CELL:
        items = [decode(r, release) for _ in range(n)]
        if n and not _is_vector(dims):
            raise ValueError('cell arrays returned from MATLAB must be 1-by-N or M-by-1')
        return items
    if kind == STRUCT:
        names = [r.text8() for _ in range(r.u32())]
        elements = [{name: decode(r, release) for name in names} for _ in range(n)]
        if not _is_scalar(dims):
            raise ValueError('only a scalar struct can be returned from MATLAB')
        return elements[0]
    if kind in (OBJECT, VALUE_OBJECT, HANDLE_OBJECT_REF):
        class_name = r.text8()
        handles = [(r.u64(), r.u64()) for _ in range(n)]
        if not handles:
            return matlab._held_object(HeldValue(kind, class_name, dims, None, 0, None))
        return matlab._held_object(HeldValue(kind, class_name, dims, handles[0][0], n, release))
    if kind == ENUM:
        r.text8()
        for _ in range(n):
            r.text8()
        raise TypeError('conversion of MATLAB data type 29 to Python not supported')
    if kind in (SPARSE_LOGICAL, SPARSE_DOUBLE):
        stored = r.u64()
        r.take(8 * stored + 8 * (dims[1] + 1) + (stored if kind == SPARSE_LOGICAL else 8 * stored))
        raise TypeError('conversion of MATLAB sparse array to Python not supported')
    if kind == PY_DATETIME:
        wall = r.double()
        zoned = r.byte()
        offset = r.double()
        if wall != wall:
            raise ValueError('conversion of MATLAB NaT to Python not supported: a datetime.datetime is a moment')
        moment = _EPOCH + datetime.timedelta(milliseconds=wall)
        if zoned:
            moment = moment.replace(tzinfo=datetime.timezone(datetime.timedelta(seconds=offset)))
        return moment
    if kind == PY_DURATION:
        length = r.double()
        if length != length:
            raise ValueError('conversion of a missing MATLAB duration to Python not supported: a datetime.timedelta is a length')
        return datetime.timedelta(milliseconds=length)
    if kind == PY_DICTIONARY:
        made = matlab.dictionary()
        if r.byte():
            keys = decode(r, release)
            values = decode(r, release)
            for key, value in zip(keys, values):
                made[key] = value
        return made
    raise TypeError('conversion of MATLAB data type %d to Python not supported' % kind)


class ErrorRecord(object):
    """A MATLAB error a call ended with: its identifier, message, stack (innermost first) and causes."""

    def __init__(self, identifier, message, frames, causes):
        self.identifier, self.message, self.frames, self.causes = identifier, message, frames, causes

    def details(self):
        """The error as R2025b's runtime writes it after 'Details: ': a line per frame, then the message."""
        lines = ''.join('\n  File %s, line %d, in %s\n' % (path, line, name.split('>')[-1]) for path, name, line in self.frames)
        return lines + self.message + '\n'


def read_error(r):
    identifier = r.text8()
    message = r.text16()
    frames = [(r.text16(), r.text16(), r.u64()) for _ in range(r.u32())]
    causes = [read_error(r) for _ in range(r.u32())]
    return ErrorRecord(identifier, message, frames, causes)
