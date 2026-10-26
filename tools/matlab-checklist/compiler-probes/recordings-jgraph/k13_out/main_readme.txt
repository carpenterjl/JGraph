k13pkg MATLAB Python Package

1. Prerequisites for Deployment 

Verify that the JGraph Runtime (0.2.3) is installed: the package finds it through the
JGRAPH_RUNTIME_ROOT environment variable, the registry key its installer writes, or its runtime\win64
folder on the PATH, and runs its functions there. The JGraph Runtime holds the matlab module the
package's functions take and give arrays of.

Verify that a Windows version of Python 3.9, 3.10, 3.11, and/or 3.12 is installed. A newer
Python is warned about and used.

2. Installing the k13pkg Package

A. Change to the directory that contains the file setup.py and the subdirectory k13pkg.
If you do not have write permissions, copy all its contents to a temporary location and
change to that directory.

B. Execute the command:

    python -m pip install . [options]

Use --user to install to your home folder, or --prefix="installdir" to install to
"installdir" and add "installdir" to the PYTHONPATH environment variable. A program can also
put this folder on its PYTHONPATH and import the package without installing it.

3. Using the k13pkg Package

The k13pkg package is on your Python path. To import it into a Python script or session,
execute:

    import k13pkg

Import the package before the matlab module: it puts the JGraph Runtime's matlab module on the
path. initialize() answers a handle, whose methods are the package's MATLAB functions.
