"""
FutureResult (compiler and SDK plan, stage K13): what a background call of a package's function
answers at once, and every call answers through: its outputs once the engine has run it, as
record-k13.ps1 measured R2025b's (k13_client: background). A result waits a second at a time, so
Ctrl+C reaches it and cancels the call; the text the call was asked to keep reaches its StringIO
objects when the result is taken.
"""

import time

from matlab_pysdk.runtime import _messages


class FutureResult(object):
    """The future result of a call of a deployed MATLAB function."""

    def __init__(self, call, nlhs, nlhs_given):
        self._call = call
        self._nlhs = nlhs
        self._nlhs_given = nlhs_given
        self._retrieved = False
        self._result = None

    def result(self, timeout=None):
        """
        The call's outputs, waiting for them up to timeout seconds (for ever when None): one output as
        itself, several as a tuple, none as None.

        Raises
            matlab_pysdk.runtime.MatlabRuntimeError - the function raised an error
            matlab_pysdk.runtime.NumberOfOutputArgumentsError - it gives fewer outputs than asked for
            matlab_pysdk.runtime.CancelledError, InterruptedError - the call was cancelled, or stopped
            RuntimeError - the timeout passed first
            TypeError, ValueError - a timeout that is not a number of seconds, or a negative one
        """
        if timeout is not None:
            if not isinstance(timeout, (int, float)):
                raise TypeError(_messages.spaced('TimeoutShouldBeIntOrFloat'))
            if timeout < 0:
                raise ValueError(_messages.spaced('TimeoutCannotBeNegative'))
        if self._retrieved:
            return self._result
        try:
            if timeout is None:
                while not self._call.wait(1):
                    pass
            else:
                deadline = time.time() + timeout
                while not self._call.done():
                    left = deadline - time.time()
                    if left <= 0:
                        break
                    self._call.wait(min(1.0, left))
                if not self._call.done():
                    raise RuntimeError(_messages.spaced('TimeoutException'))
            from matlab_pysdk.runtime import NumberOfOutputArgumentsError
            try:
                self._result = self._call.bridge.result(self._call)
            except NumberOfOutputArgumentsError:
                if self._nlhs_given:
                    raise NumberOfOutputArgumentsError(_messages.spaced('WrongNargout', '({0}).'.format(self._nlhs))) from None
                raise NumberOfOutputArgumentsError(_messages.spaced('NargoutNotSet')) from None
            self._retrieved = True
            return self._result
        except KeyboardInterrupt:
            cancelled = self.cancel()
            print(_messages.spaced('CommandCancelled'))
            return cancelled

    def cancel(self):
        """Cancels the call: one not started never runs, a running one stops at its next statement. True when it was cancelled."""
        return self._call.cancel()

    def cancelled(self):
        """Whether the call was cancelled."""
        return self._call.cancelled

    def done(self):
        """Whether the call has ended: with its outputs, an error, or cancelled."""
        return self._call.done()

    def __del__(self):
        call = getattr(self, '_call', None)
        if call is not None:
            try:
                call.forget()
            except Exception:  # an interpreter that is shutting down may have let the runtime go already
                pass
