"""
The JGraph Runtime's native bridge for a Python package (compiler and SDK plan, stage K13): the
entry points jgrt.dll gives the C++ shared library API (jgraph/jgrt_cpp.h), called through ctypes,
where R2025b's packages call its own extension module. There is one per process: the runtime starts
once, each package instance opens its archive in a session of its own (jgrtPyOpen), and every call
runs on the engine's thread after the calls started before it, its text and its end reported back
through two callbacks.
"""

import ctypes
import threading
import xml.etree.ElementTree as ElementTree
import zipfile

from matlab_pysdk.runtime import _messages
from matlab_pysdk.runtime import _wire

_WRITE = ctypes.CFUNCTYPE(None, ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_size_t)
_DONE = ctypes.CFUNCTYPE(None, ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_size_t)

# How a call ended (jgrtCppDoneFcn's status).
DONE, CANCELLED, INTERRUPTED, MATLAB_ERROR, SYNTAX_ERROR, OTHER, STOPPED, REFUSED = range(8)

# The options R2025b's Python runtime starts with; any other is warned about and left out, as its runtime does.
_OPTIONS = ('-nojvm', '-nodisplay', '-singleCompThread')

_ENDED = ('The JGraph Runtime has ended in this process: terminate_runtime() was called, '
          'and the runtime cannot start again in it.')


def _public_functions(archive):
    """The functions a package's archive lets Python call: those its manifest names."""
    with zipfile.ZipFile(archive) as zipped:
        root = ElementTree.fromstring(zipped.read('.META/manifest.xml'))
    return frozenset(f.findtext('name') for f in root.findall('function'))


class Call(object):
    """A call on its way: its task, whether it has ended and how, and the text it was asked to keep."""

    def __init__(self, bridge, nlhs, out, err):
        self.bridge = bridge
        self.nlhs = nlhs
        self.out = out
        self.err = err
        self.written = ([], [])
        self.ended = threading.Event()
        self.status = None
        self.data = b''
        self.task = 0
        self.cancelled = False

    def wait(self, seconds):
        return self.ended.wait(seconds)

    def done(self):
        return self.ended.is_set()

    def cancel(self):
        """A call not started never runs, a running one is interrupted at its next statement: True; one ended: False."""
        if self.ended.is_set() or not self.task:
            return False
        if self.bridge.dll.jgrtCppCancel(self.task, 1) == 1:
            self.cancelled = True
            return True
        return False

    def forget(self):
        task, self.task = self.task, 0
        if task and self.bridge.phase != 2:
            self.bridge.dll.jgrtCppForget(task)


class Bridge(object):
    """jgrt.dll, loaded once: the application's phase, its instances, and the calls under way."""

    def __init__(self, path):
        self.path = path
        dll = ctypes.CDLL(path)
        dll.jgrtCppStart.argtypes = [ctypes.c_int, ctypes.POINTER(ctypes.c_wchar_p), ctypes.c_size_t, ctypes.POINTER(ctypes.c_void_p)]
        dll.jgrtCppStart.restype = ctypes.c_int
        dll.jgrtCppStop.argtypes = []
        dll.jgrtCppStop.restype = None
        dll.jgrtPyOpen.argtypes = [ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_void_p)]
        dll.jgrtPyOpen.restype = ctypes.c_uint64
        dll.jgrtCppClose.argtypes = [ctypes.c_uint64]
        dll.jgrtCppClose.restype = None
        dll.jgrtCppWaitForFigures.argtypes = [ctypes.c_uint64]
        dll.jgrtCppWaitForFigures.restype = None
        dll.jgrtCppCall.argtypes = [ctypes.c_uint64, ctypes.c_wchar_p, ctypes.c_size_t, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_void_p,
                                    _WRITE, ctypes.c_int, ctypes.c_int, _DONE, ctypes.POINTER(ctypes.c_void_p)]
        dll.jgrtCppCall.restype = ctypes.c_uint64
        dll.jgrtCppCancel.argtypes = [ctypes.c_uint64, ctypes.c_int]
        dll.jgrtCppCancel.restype = ctypes.c_int
        dll.jgrtCppForget.argtypes = [ctypes.c_uint64]
        dll.jgrtCppForget.restype = None
        dll.jgrtCppRelease.argtypes = [ctypes.c_uint64]
        dll.jgrtCppRelease.restype = None
        dll.jgrtCppFree.argtypes = [ctypes.c_void_p]
        dll.jgrtCppFree.restype = None
        self.dll = dll
        self.phase = 0  # 0 not started, 1 running, 2 ended
        self.options = None  # initialize_runtime()'s, for the start
        self.functions = {}
        self._calls = {}
        self._last = 0
        self._lock = threading.Lock()
        # The callbacks live as long as the bridge: the engine may call them until the process ends.
        self._write = _WRITE(self._on_write)
        self._done = _DONE(self._on_done)

    def _text(self, pointer):
        if not pointer.value:
            return ''
        try:
            return ctypes.wstring_at(pointer.value)
        finally:
            self.dll.jgrtCppFree(pointer.value)

    # --- The application -----------------------------------------------------------------------

    def initialize_application(self, options):
        """initialize_runtime(): the options the runtime starts with at the first initialize(), as
        R2025b's starts then (k13_client: runtime bogus warned after the call); later calls are let be."""
        options = list(options or [])
        for i, option in enumerate(options):
            if not isinstance(option, str):
                raise TypeError(_messages.text('OptionIsNotAString', i))
        if self.phase == 2:
            raise SystemError(_ENDED)
        if self.phase == 0 and self.options is None:
            self.options = options

    def _start(self, options):
        kept = []
        i = 0
        while i < len(options):
            option = options[i]
            if option == '-logfile' and i + 1 < len(options):
                kept += options[i:i + 2]
                i += 2
                continue
            if option in _OPTIONS:
                kept.append(option)
            else:
                print('Warning: Unrecognized command line option: %s.' % option.lstrip('-'))
            i += 1
        options = kept
        texts = (ctypes.c_wchar_p * max(len(options), 1))(*options)
        error = ctypes.c_void_p()
        started = self.dll.jgrtCppStart(0, texts, len(options), ctypes.byref(error))
        reason = self._text(error)
        if started == 2:
            self.phase = 2
            raise SystemError(_ENDED)
        if started == 3:
            raise RuntimeError(_messages.spaced('InitErrorWithDetails', reason))
        self.phase = 1

    def terminate_application(self):
        """terminate_runtime(): the application ends, and cannot start again in the process."""
        if self.phase == 1:
            self.dll.jgrtCppStop()
        self.phase = 2

    # --- A package's instances -----------------------------------------------------------------

    def start_instance(self, archive):
        """A package instance: its archive opened in a session of its own, the runtime started first if need be."""
        if self.phase == 2:
            raise SystemError(_ENDED)
        if self.phase == 0:
            self._start(self.options or [])
        error = ctypes.c_void_p()
        handle = self.dll.jgrtPyOpen(archive, ctypes.byref(error))
        reason = self._text(error)
        if not handle:
            raise RuntimeError(_messages.spaced('InitErrorWithDetails', reason or archive))
        self.functions[handle] = _public_functions(archive)
        return handle

    def shutdown_instance(self, handle):
        self.functions.pop(handle, None)
        if self.phase == 1:
            self.dll.jgrtCppClose(handle)

    def wait_for_figures(self, handle):
        if self.phase == 1:
            self.dll.jgrtCppWaitForFigures(handle)

    def release(self, value):
        """Lets go of a value the engine keeps for a matlab.object."""
        if self.phase == 1:
            self.dll.jgrtCppRelease(value)

    # --- Calls -----------------------------------------------------------------------------------

    def call(self, handle, name, nlhs, args, out=None, err=None):
        """A call of name started: the inputs converted as R2025b converts them, a name the package does
        not have refused as it refuses one (k13_client: unknown*), the text kept for out and err if given."""
        if self.phase != 1 or handle not in self.functions:
            raise SystemError(_ENDED)
        if name not in self.functions[handle]:
            raise SystemError(_messages.text('GeneralPySDKError', _messages.text(
                'ErrorDuringFevalWithNameAndDetails', name, _messages.text('FunctionNotFoundInPyPkg', name))))
        w = _wire.Writer()
        for arg in args:
            _wire.encode(w, arg)
        call = Call(self, nlhs, out, err)
        with self._lock:
            self._last += 1
            context = self._last
            self._calls[context] = call
        inputs = bytes(w.bytes)
        refused = ctypes.c_void_p()
        task = self.dll.jgrtCppCall(handle, name, nlhs, inputs, len(inputs), context, self._write, 1 if out is not None else 0,
                                    1 if err is not None else 0, self._done, ctypes.byref(refused))
        reason = self._text(refused)
        if not task:
            with self._lock:
                self._calls.pop(context, None)
            raise SystemError(_messages.text('GeneralPySDKError', _messages.text('ErrorDuringFevalWithNameAndDetails', name, reason)))
        call.task = task
        return call

    def _on_write(self, context, stream, text, length):
        try:
            call = self._calls.get(context)
            if call is not None and length:
                call.written[0 if stream == 1 else 1].append(ctypes.wstring_at(text, length))
        except Exception:  # nothing raised may go back into the engine
            pass

    def _on_done(self, context, status, data, length):
        try:
            with self._lock:
                call = self._calls.pop(context, None)
            if call is not None:
                call.status = status
                call.data = ctypes.string_at(data, length) if length else b''
                call.ended.set()
        except Exception:  # nothing raised may go back into the engine
            pass

    def result(self, call):
        """A call's outputs as Python values, or its error raised as R2025b's runtime raises it; the text
        it was asked to keep is written to its StringIO objects first, as R2025b writes it then."""
        from matlab_pysdk import runtime
        if call.out is not None and call.written[0]:
            call.out.write(''.join(call.written[0]))
            del call.written[0][:]
        if call.err is not None and call.written[1]:
            call.err.write(''.join(call.written[1]))
            del call.written[1][:]
        r = _wire.Reader(call.data)
        if call.status == DONE:
            values = []
            while not r.at_end:
                values.append(_wire.decode(r, self.release))
            if call.nlhs == 0:
                return None
            return values[0] if call.nlhs == 1 else tuple(values)
        if call.status in (MATLAB_ERROR, SYNTAX_ERROR):
            record = _wire.read_error(r)
            if record.identifier in ('MATLAB:TooManyOutputs', 'MATLAB:maxlhs'):
                raise runtime.NumberOfOutputArgumentsError(record.message)
            raise runtime.MatlabRuntimeError(_messages.text('FEvalResultError', record.details()))
        if call.status == CANCELLED:
            raise runtime.CancelledError(_messages.text('FEvalResultError', _messages.text('CancelledException')))
        if call.status == INTERRUPTED:
            raise runtime.InterruptedError(_messages.text('FEvalResultError', _messages.text('InterruptedException')))
        reason = r.text8() if call.data else ''
        raise SystemError(_messages.text('GeneralPySDKError', reason))
