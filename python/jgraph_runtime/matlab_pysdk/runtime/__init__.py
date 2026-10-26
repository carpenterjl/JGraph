"""
The runtime a package of deployed MATLAB functions calls (compiler and SDK plan, stage K13, decision
KO3): R2025b's matlab_pysdk.runtime by its names, over the JGraph Runtime's native bridge jgrt.dll.
A package's __init__.py imports it from the runtime's python folder and attaches the bridge it found;
a program does not import it itself. The runtime ends when the program does.
"""

import atexit

from matlab_pysdk.runtime.deployablepackage import DeployablePackage


class MatlabRuntimeError(Exception):
    """An error a deployed function raised, its stack and message in the text."""


class NumberOfOutputArgumentsError(Exception):
    """A call asked for more outputs than the function gives."""


class InterruptedError(Exception):
    """A call stopped while it ran, by its cancel()."""


class CancelledError(Exception):
    """A call cancelled before it ran."""


class MatlabExecutionError(Exception):
    """An error running MATLAB code, under the name R2025b's runtime gives it."""


class TimeoutError(Exception):
    """A wait that ran out, under the name R2025b's runtime gives it."""


# The release the package follows and the JGraph version of the runtime first attached: a package
# built by another version cannot share the process with it.
_runtime_version_w_dots = ''
_jgraph_version = None
_the_bridge = None


def _attach(bridge_path, jgraph_version):
    """Loads jgrt.dll from bridge_path once, for the packages of jgraph_version's runtime."""
    global _the_bridge, _jgraph_version
    if _the_bridge is not None:
        if jgraph_version != _jgraph_version:
            raise RuntimeError('Runtime version of package ({0}) does not match runtime version of previously loaded package ({1})'.format(
                jgraph_version, _jgraph_version))
        return
    from matlab_pysdk.runtime._jgrt import Bridge
    _the_bridge = Bridge(bridge_path)
    _jgraph_version = jgraph_version


def _bridge():
    if _the_bridge is None:
        raise RuntimeError('The JGraph Runtime is not attached: import a package of deployed MATLAB functions first.')
    return _the_bridge


@atexit.register
def __exit_application():
    if _the_bridge is not None:
        _the_bridge.terminate_application()
