"""
DeployablePackage (compiler and SDK plan, stage K13): what a package's initialize() answers, an
instance of the package open in the JGraph Runtime in a session of its own, as each of R2025b's is
(record-k13.ps1, k13_client: life). Its attributes are the package's functions; terminate(), exit()
and quit() close it, as the end of the program and its garbage collection do.
"""

import os.path
import weakref

from matlab_pysdk.runtime.deployablefunc import DeployableFunc


class DeployablePackage(object):
    """A package's functions, open in the JGraph Runtime."""

    def __init__(self, path_initializer_handle, name, module_path):
        self.__package_opened = False
        self.__handle = None
        self.__path_initializer_handle = weakref.ref(path_initializer_handle)
        self.__name = name
        folder = os.path.dirname(os.path.realpath(module_path))
        self.__ctf_path = os.path.join(folder, '{0}.ctf'.format(os.path.basename(folder)))

    @property
    def name(self):
        """The package's name."""
        return self.__name

    def initialize(self):
        """Opens the package's archive in a session of its own."""
        from matlab_pysdk import runtime
        bridge = runtime._bridge()
        self.__handle = bridge.start_instance(self.__ctf_path)
        self.__bridge = bridge
        self.__package_opened = True

    def __del__(self):
        self.terminate()

    def __getattr__(self, name):
        # A name of the class's own that is not set yet is not a function of the package.
        if name.startswith('_DeployablePackage__'):
            raise AttributeError(name)
        if name.startswith('__'):
            return self.name
        if not self.__package_opened:
            raise RuntimeError('{0}() cannot be called after terminate() has been called on the package {1}'.format(name, self.__name))
        return DeployableFunc(self, name, self.__bridge, self.__handle)

    def wait_for_figures_to_close(self):
        """Waits until no figure the package's functions showed is on screen."""
        if self.__package_opened:
            self.__bridge.wait_for_figures(self.__handle)

    def terminate(self):
        """Closes the package, and the session it was open in."""
        if getattr(self, '_DeployablePackage__package_opened', False):
            self.__package_opened = False
            try:
                self.__bridge.shutdown_instance(self.__handle)
            except Exception:  # the runtime may have ended first, at the end of the program
                pass
        initializer = self.__path_initializer_handle() if hasattr(self, '_DeployablePackage__path_initializer_handle') else None
        if initializer is not None:
            initializer.instances_of_this_package.discard(weakref.ref(self))

    def exit(self):
        """Equivalent to terminate()."""
        self.terminate()

    def quit(self):
        """Equivalent to terminate()."""
        self.terminate()
