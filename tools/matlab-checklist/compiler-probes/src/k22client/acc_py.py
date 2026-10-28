"""The acceptance set's Python client (compiler and SDK plan, stage K22) of the package accpkg built
from src\\k22 by R2025b or by JGraph, run unchanged against either with the package on PYTHONPATH.
One label<TAB>result line per question, so the two transcripts compare line for line."""
import re
import sys


def say(label, text):
    print('%s\t%s' % (label, text), flush=True)


def tname(v):
    t = type(v)
    return t.__name__ if t.__module__ == 'builtins' else '%s.%s' % (t.__module__, t.__name__)


def desc(v):
    if v is None:
        return 'None'
    if isinstance(v, dict):
        return 'dict{' + ', '.join('%r: %s' % (k, desc(v[k])) for k in sorted(v)) + '}'
    if isinstance(v, (list, tuple)):
        return '%s[%s]' % (tname(v), ', '.join(desc(x) for x in v))
    if isinstance(v, (bool, int, float, str)):
        return '%s %r' % (tname(v), v)
    if hasattr(v, 'size') and hasattr(v, '_data'):
        return '%s size=%r %s' % (tname(v), v.size, re.sub(r'0x[0-9A-Fa-f]+', '0x<ADDR>', repr(v)))
    return '%s %s' % (tname(v), re.sub(r'0x[0-9A-Fa-f]+', '0x<ADDR>', repr(v)))


def case(label, fn):
    try:
        say(label, desc(fn()))
    except BaseException as e:  # noqa: B902 - every failure is a line
        say(label, 'ERR %s | %s' % (tname(e), str(e).replace('\r', '').replace('\n', '\\n')))


case('import', lambda: __import__('accpkg').__name__)
import accpkg  # noqa: E402
import matlab  # noqa: E402

h = accpkg.initialize()
say('initialize', tname(h))
case('quiet', lambda: h.acc_quiet(nargout=0))
case('add', lambda: h.acc_add(2.0, 3.0))
case('add.int', lambda: h.acc_add(2, 3))
case('stats', lambda: h.acc_stats(matlab.double([[3.0, 1.0, 4.0, 1.0, 5.0]])))
case('stats.mean', lambda: h.acc_stats(matlab.double([[3.0, 1.0, 4.0, 1.0, 5.0]]))['mean'])
case('greet', lambda: h.acc_greet('Ada'))
case('scale', lambda: h.acc_scale(matlab.double([[1.0, 2.0], [3.0, 4.0]]), 10.0))
case('scale.list', lambda: [list(r) for r in h.acc_scale(matlab.double([[1.0, 2.0], [3.0, 4.0]]), 10.0)])
case('count.1', lambda: h.acc_count())
case('count.2', lambda: h.acc_count())
case('fail', lambda: h.acc_fail('from Python', nargout=0))
case('add.wrongclass', lambda: h.acc_add('x', 3.0))
case('count.3', lambda: h.acc_count())
case('terminate', lambda: h.terminate())
say('end', 'ok')
