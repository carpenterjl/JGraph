"""
DeployableFunc (compiler and SDK plan, stage K13): a package's function, as an attribute of its
handle names it, called as R2025b's are called (record-k13.ps1, k13_client: calls): its inputs
positional, and the keywords nargout, stdout, stderr and background (async the deprecated name),
each checked in R2025b's order and words. An attribute of a function names a dotted one.
"""

import io
import warnings
import weakref

from matlab_pysdk.runtime import _messages
from matlab_pysdk.runtime.futureresult import FutureResult


class DeployableFunc(object):
    """A deployed MATLAB function a package's handle calls."""

    def __init__(self, package, name, bridge, handle):
        # Kept in the instance's dict, so that reading them never reaches __getattr__.
        self.__dict__['_package'] = weakref.ref(package)
        self.__dict__['_name'] = name
        self.__dict__['_bridge'] = bridge
        self.__dict__['_handle'] = handle

    def __getattr__(self, name):
        return DeployableFunc(self._package(), '{0}.{1}'.format(self._name, name), self._bridge, self._handle)

    def __setattr__(self, name, value):
        raise AttributeError(_messages.spaced('AttrCannotBeAddedToFunc'))

    def __call__(self, *args, **kwargs):
        if self._package() is None:
            raise RuntimeError('Could not find package')
        given = 'nargout' in kwargs
        nlhs = kwargs.pop('nargout', 1)
        if given:
            if not isinstance(nlhs, int):
                raise TypeError(_messages.spaced('NargoutMustBeInt'))
            if nlhs < 0:
                raise ValueError(_messages.spaced('NargoutCannotBeLessThanZero'))
        out = kwargs.pop('stdout', None)
        err = kwargs.pop('stderr', None)
        background = _background(kwargs)
        if kwargs:
            raise TypeError(_messages.spaced('InvalidKwargs', ', '.join(sorted(kwargs.keys()))))
        if out is not None and not isinstance(out, io.StringIO):
            raise TypeError(_messages.spaced('StringIOForConOut'))
        if err is not None and not isinstance(err, io.StringIO):
            raise TypeError(_messages.spaced('StringIOForConErr'))
        call = self._bridge.call(self._handle, self._name, int(nlhs), args, out, err)
        future = FutureResult(call, int(nlhs), given)
        return future if background else future.result()


def _background(kwargs):
    """background, or async with a deprecation warning; not both, and a bool either way."""
    if 'background' in kwargs and 'async' in kwargs:
        raise RuntimeError(_messages.spaced('EitherBackgroundOrAsync'))
    background = False
    if 'background' in kwargs:
        background = kwargs.pop('background')
        if not isinstance(background, bool):
            raise TypeError(_messages.spaced('BackgroundMustBeBool'))
    if 'async' in kwargs:
        background = kwargs.pop('async')
        if not isinstance(background, bool):
            raise TypeError(_messages.spaced('AsyncMustBeBool'))
        warnings.warn(_messages.spaced('AsyncWillBeRemoved'), DeprecationWarning)
    return background
