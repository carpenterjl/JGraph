using System.Text;

namespace JGraph.Deploy;

/// <summary>A Python package build's files: what <c>compiler.build.Results.Files</c> lists first, and the archive's manifest.</summary>
/// <param name="Files">The package's top folder and <c>setup.py</c>, as R2025b's results list them (k13_build: main.Files).</param>
/// <param name="Manifest">The archive's manifest.</param>
public sealed record PythonPackageBuild(IReadOnlyList<string> Files, CtfManifest Manifest);

/// <summary>
/// A Python package (compiler and SDK plan, stage K13), as R2025b's mcc <c>-W python:&lt;name&gt;</c> and
/// <c>compiler.build.pythonPackage</c> make one (<c>record-k13.ps1</c>, <c>recordings\k13_build</c>):
/// a folder per part of the package's dotted name, the outer ones namespace packages, the innermost
/// holding the deployable archive <c>&lt;last part&gt;.ctf</c> and the <c>__init__.py</c> a Python
/// program imports; <c>setup.py</c> and <c>pyproject.toml</c> to install it with pip; and a readme.
/// Nothing is compiled. The <c>__init__.py</c> is JGraph's own: it finds the JGraph Runtime of the
/// building JGraph's version and puts the runtime's python folder (the <c>matlab</c> module and the
/// <c>matlab_pysdk</c> runtime, which reaches the engine through jgrt.dll) on the module path, then
/// gives R2025b's package functions: <c>initialize</c>, <c>initialize_runtime</c> and
/// <c>terminate_runtime</c>, with R2025b's checks of the interpreter's version.
/// </summary>
public static class PythonPackage
{
    /// <summary>The Python versions R2025b's packages take; a newer one is warned about and taken.</summary>
    public static readonly string[] SupportedPythonVersions = ["3.9", "3.10", "3.11", "3.12"];

    /// <summary>The parts of a package's dotted name, empty ones dropped as R2025b drops them (k13_build: mcc_dotdot).</summary>
    public static string[] Parts(string packageName) => packageName.Split('.', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The package's own name: the last part of a dotted one, which names its archive and its readme.</summary>
    public static string ShortName(string packageName) => Parts(packageName)[^1];

    /// <summary>The innermost folder, where the archive and the package's <c>__init__.py</c> are.</summary>
    public static string PackageFolder(string outputFolder, string packageName) =>
        Path.Combine([Path.GetFullPath(outputFolder), .. Parts(packageName)]);

    /// <summary>
    /// Writes the package: the archive, the <c>__init__.py</c> files, <c>setup.py</c>,
    /// <c>pyproject.toml</c> and <c>readme.txt</c>, each with CR LF as R2025b writes them; answers the
    /// top folder and <c>setup.py</c> (k13_build: main.Files) and the manifest.
    /// </summary>
    /// <param name="build">The archive's contents.</param>
    /// <param name="outputFolder">The folder the build writes into.</param>
    /// <param name="packageName">The package's full name, its dotted parts as R2025b keeps them.</param>
    /// <param name="written">Each file written, in R2025b's order, for <c>-v</c>'s lines.</param>
    public static PythonPackageBuild Write(CtfBuild build, string outputFolder, string packageName, Action<string>? written = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        string[] parts = Parts(packageName);
        if (parts.Length == 0)
        {
            throw new ArgumentException("A Python package needs a name.", nameof(packageName));
        }

        string full = string.Join('.', parts);
        string folder = Path.GetFullPath(outputFolder);
        string inner = PackageFolder(folder, full);
        Directory.CreateDirectory(inner);
        var utf8 = new UTF8Encoding(false);

        CtfManifest manifest;
        using (FileStream stream = File.Create(Path.Combine(inner, parts[^1] + ".ctf")))
        {
            manifest = CtfArchiveWriter.Write(stream, build with { TargetType = CtfTargetType.PythonPackage });
        }

        // The outer parts are namespace packages, so two packages may share them (R2025b's 121-byte init).
        string level = folder;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            level = Path.Combine(level, parts[i]);
            File.WriteAllText(Path.Combine(level, "__init__.py"), Crlf(NamespaceInit(full)), utf8);
        }

        void Emit(string path, string text)
        {
            File.WriteAllText(path, Crlf(text), utf8);
            written?.Invoke(path);
        }

        Emit(Path.Combine(inner, "__init__.py"), PackageInit(full, RuntimeLayout.CurrentVersion));
        Emit(Path.Combine(folder, "setup.py"), Setup(full));
        Emit(Path.Combine(folder, "pyproject.toml"), Pyproject(full));
        Emit(Path.Combine(folder, "readme.txt"), Readme(full));
        return new PythonPackageBuild([Path.Combine(folder, parts[0]), Path.Combine(folder, "setup.py")], manifest);
    }

    /// <summary>An outer part's <c>__init__.py</c>: a namespace package, as R2025b's is.</summary>
    public static string NamespaceInit(string packageName) =>
        $"""

        # A namespace level of the Python package {packageName}, generated by JGraph's mcc.

        from pkgutil import extend_path
        __path__ = extend_path(__path__, __name__)

        """;

    /// <summary>
    /// The package's <c>__init__.py</c>, in JGraph's words: R2025b's module functions and its checks of
    /// the interpreter's version (k13_client: version), over a search for the JGraph Runtime of
    /// <paramref name="version"/> in the places a C program's search and the launcher's take.
    /// </summary>
    public static string PackageInit(string packageName, string version) =>
        PackageInitTemplate
            .Replace("@@PACKAGE@@", packageName, StringComparison.Ordinal)
            .Replace("@@VERSION@@", version, StringComparison.Ordinal)
            .Replace("@@RELEASE@@", RuntimeOptions.Release, StringComparison.Ordinal)
            .Replace("@@PYTHONS@@", string.Join(", ", SupportedPythonVersions.Select(static v => "'" + v.Replace('.', '_') + "'")), StringComparison.Ordinal);

    private const string PackageInitTemplate =
        """
        # The Python package @@PACKAGE@@ of deployed MATLAB functions, generated by JGraph @@VERSION@@'s mcc.
        # It runs on the JGraph Runtime @@VERSION@@: the folder JGRAPH_RUNTIME_ROOT names, else the one its
        # installer registered, else the one whose runtime\win64 folder, where jgrt.dll is, is on the PATH.
        # Import it before the matlab module, which comes from that runtime.

        """ + "\"\"\" Package for executing deployed MATLAB functions \"\"\"" + """


        import atexit
        import importlib
        import json
        import os
        import sys
        import warnings
        import weakref


        class _PathInitializer(object):
            SUPPORTED_PYTHON_VERSIONS = [@@PYTHONS@@]
            RUNTIME_VERSION_W_DOTS = '@@RELEASE@@'
            JGRAPH_VERSION = '@@VERSION@@'
            PACKAGE_NAME = '@@PACKAGE@@'

            def __init__(self):
                self.interpreter_version = self.check_interpreter_version()
                self.runtime_root, self.python_dir, self.bridge_path = self.find_runtime()
                self.mr_handle = None
                self.ml_handle = None
                self.instances_of_this_package = set()

            def check_interpreter_version(self):
                # The versions R2025b's packages take; a newer one is warned about and taken.
                ver = sys.version_info
                version = '{0}_{1}'.format(ver[0], ver[1])
                version_with_dot = version.replace('_', '.')
                if version in self.SUPPORTED_PYTHON_VERSIONS:
                    return version
                if ver[1] > 12:
                    warnings.warn('Python versions 3.9, 3.10, 3.11, and 3.12 are supported, but your version of Python is %s' % version_with_dot)
                    return version
                raise EnvironmentError('Python {0} is not supported.'.format(version_with_dot))

            def find_runtime(self):
                # The runtime's root, its python folder and its jgrt.dll, from the first place that has
                # this version's: a runtime of another version is passed over, and named if none is found.
                passed = []
                for folder, where in self.candidates():
                    found = self.runtime_at(folder)
                    if found is None:
                        continue
                    root, python_dir, bridge, version = found
                    if version is not None and version != self.JGRAPH_VERSION:
                        passed.append('{0} (from {1}) is the JGraph Runtime {2}'.format(root, where, version))
                        continue
                    return root, python_dir, bridge
                message = ('Could not find the JGraph Runtime {0}, which the package {1} runs on. Install it, set '
                           'JGRAPH_RUNTIME_ROOT to its folder, or put its runtime{2}win64 folder on the PATH.').format(
                               self.JGRAPH_VERSION, self.PACKAGE_NAME, os.sep)
                if passed:
                    message += ' ' + '; '.join(passed) + '.'
                raise RuntimeError(message)

            def candidates(self):
                root = os.environ.get('JGRAPH_RUNTIME_ROOT')
                if root:
                    yield root, 'JGRAPH_RUNTIME_ROOT'
                for location in self.registered():
                    yield location, 'the registry'
                for folder in os.environ.get('PATH', '').split(os.pathsep):
                    if folder and os.path.isfile(os.path.join(folder, 'jgrt.dll')):
                        yield folder, 'the PATH'

            def registered(self):
                # The runtime's installer registers each version under the machine's key or the user's.
                try:
                    import winreg
                except ImportError:
                    return
                for hive in (winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER):
                    try:
                        with winreg.OpenKey(hive, 'SOFTWARE\\JGraph\\JGraph Runtime\\' + self.JGRAPH_VERSION) as key:
                            location = winreg.QueryValueEx(key, 'Location')[0]
                    except OSError:
                        continue
                    if location:
                        yield location

            @staticmethod
            def runtime_at(folder):
                # A runtime's root or its runtime\win64 folder (the root's marker names its version), or
                # a folder holding the engine, jgrt.dll beside it and the python folder, as JGraph's own does.
                folder = os.path.normpath(folder)
                for root, bridge in ((folder, os.path.join(folder, 'runtime', 'win64', 'jgrt.dll')),
                                     (os.path.dirname(os.path.dirname(folder)), os.path.join(folder, 'jgrt.dll'))):
                    marker = os.path.join(root, 'jgraph-runtime.json')
                    if os.path.isfile(marker) and os.path.isfile(bridge):
                        try:
                            with open(marker, encoding='utf-8') as text:
                                version = json.load(text).get('version')
                        except (OSError, ValueError):
                            continue
                        return root, os.path.join(root, 'python'), bridge, version
                bridge = os.path.join(folder, 'jgrt.dll')
                if os.path.isfile(bridge) and os.path.isdir(os.path.join(folder, 'python', 'matlab_pysdk')):
                    return folder, os.path.join(folder, 'python'), bridge, None
                return None

            def update_paths(self):
                if self.python_dir not in sys.path:
                    sys.path.insert(0, self.python_dir)

            def import_matlab_pysdk_runtime(self):
                self.mr_handle = importlib.import_module('matlab_pysdk.runtime')
                self.mr_handle._attach(self.bridge_path, self.JGRAPH_VERSION)

            def import_matlab(self):
                self.ml_handle = importlib.import_module('matlab')

            def initialize_package(self):
                package_handle = self.mr_handle.DeployablePackage(self, self.PACKAGE_NAME, __file__)
                self.instances_of_this_package.add(weakref.ref(package_handle))
                package_handle.initialize()
                return package_handle

            def initialize_runtime(self, option_list):
                self.mr_handle._bridge().initialize_application(option_list)

            def terminate_runtime(self):
                self.mr_handle._bridge().terminate_application()


        # An exception here, such as no runtime found, is the import's.
        _pir = _PathInitializer()
        _pir.update_paths()
        _pir.import_matlab_pysdk_runtime()
        _pir.import_matlab()


        def initialize():
            # Opens the package's functions in the JGraph Runtime, which starts with no options if
            # initialize_runtime() has not started it, and answers the handle to call them on; terminate(),
            # exit() and quit() on it close them, as the end of the program does.
            return _pir.initialize_package()


        def initialize_runtime(option_list):
            # Starts the JGraph Runtime with options, a list or tuple of strings (-nojvm, -nodisplay), for
            # every package in the process; only before the first initialize().
            if option_list:
                if not isinstance(option_list, list) and not isinstance(option_list, tuple):
                    raise SyntaxError('initialize_runtime takes a list or tuple of strings.')
            _pir.initialize_runtime(option_list)


        def terminate_runtime():
            # Ends the JGraph Runtime: no package in this process can call it afterwards.
            _pir.terminate_runtime()


        @atexit.register
        def __exit_packages():
            for package in list(_pir.instances_of_this_package):
                if package() is not None:
                    package().terminate()

        """;

    /// <summary><c>setup.py</c>: pip's installation of the package, under R2025b's distribution name and version (k13_build: main.setup).</summary>
    public static string Setup(string packageName)
    {
        string[] parts = Parts(packageName);
        var packages = new List<string>();
        for (int i = 1; i <= parts.Length; i++)
        {
            packages.Add(string.Join('.', parts.Take(i)));
        }

        string list = string.Join(",\n", packages.Select(static p => $"            '{p}'"));
        return $$"""
            # Installs the Python package {{packageName}}, generated by JGraph's mcc:
            #     python -m pip install .
            # from the folder this file is in.

            try:
                from setuptools import setup
            except ImportError:
                raise EnvironmentError("Installation failed. Install setuptools using 'python -m pip install setuptools', then try again.")

            if __name__ == '__main__':
                setup(
                    name='{{packageName}}-R2025b',
                    version='{{RuntimeOptions.Release}}',
                    description='A Python interface to {{packageName}}',
                    platforms=['Windows'],
                    packages=[
            {{list}}
                    ],
                    package_data={'{{packageName}}': ['*.ctf']},
                )

            """;
    }

    /// <summary><c>pyproject.toml</c>: the build system pip runs setup.py with, as R2025b's names it.</summary>
    public static string Pyproject(string packageName) =>
        $"""
        # How pip builds the Python package {packageName}, generated by JGraph's mcc.

        [build-system]
        requires = ["setuptools >= 42", "wheel"]
        build-backend = "setuptools.build_meta"

        """;

    /// <summary>readme.txt: JGraph's own text under R2025b's title line and three numbered sections (k13_build: main.readme).</summary>
    public static string Readme(string packageName)
    {
        string name = ShortName(packageName);
        string top = Parts(packageName)[0];
        return $"""
            {name} MATLAB Python Package

            {"1. Prerequisites for Deployment "}

            Verify that the JGraph Runtime ({RuntimeLayout.CurrentVersion}) is installed: the package finds it through the
            JGRAPH_RUNTIME_ROOT environment variable, the registry key its installer writes, or its runtime\win64
            folder on the PATH, and runs its functions there. The JGraph Runtime holds the matlab module the
            package's functions take and give arrays of.

            Verify that a Windows version of Python {string.Join(", ", SupportedPythonVersions[..^1])}, and/or {SupportedPythonVersions[^1]} is installed. A newer
            Python is warned about and used.

            2. Installing the {name} Package

            A. Change to the directory that contains the file setup.py and the subdirectory {top}.
            If you do not have write permissions, copy all its contents to a temporary location and
            change to that directory.

            B. Execute the command:

                python -m pip install . [options]

            Use --user to install to your home folder, or --prefix="installdir" to install to
            "installdir" and add "installdir" to the PYTHONPATH environment variable. A program can also
            put this folder on its PYTHONPATH and import the package without installing it.

            3. Using the {name} Package

            The {name} package is on your Python path. To import it into a Python script or session,
            execute:

                import {packageName}

            Import the package before the matlab module: it puts the JGraph Runtime's matlab module on the
            path. initialize() answers a handle, whose methods are the package's MATLAB functions.

            """;
    }

    /// <summary>Writes <c>GettingStarted.html</c> into <paramref name="folder"/> for the package <paramref name="packageName"/>, and answers its path.</summary>
    public static string WriteGettingStarted(string folder, string packageName)
    {
        string path = Path.Combine(folder, "GettingStarted.html");
        File.WriteAllText(path, GettingStarted(packageName), new UTF8Encoding(false));
        return path;
    }

    /// <summary><c>GettingStarted.html</c>, in JGraph's words (R2025b's has line feeds, k13_build: main.file.GettingStarted.html).</summary>
    private static string GettingStarted(string packageName) =>
        $"""
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <title>Getting Started with the {ShortName(packageName)} Python Package</title>
        </head>
        <body>
        <h1>Getting Started with the {ShortName(packageName)} Python Package</h1>
        <ol>
        <li>Install the JGraph Runtime {RuntimeLayout.CurrentVersion}, or set JGRAPH_RUNTIME_ROOT to its folder, or put its runtime\win64 folder on the PATH.</li>
        <li>Install the package from this folder with <code>python -m pip install .</code>, or put this folder on PYTHONPATH.</li>
        <li>In Python, import the package, then the matlab module, and call the functions through the handle <code>initialize()</code> answers:
        <pre>import {packageName}
        import matlab
        handle = {packageName}.initialize()
        result = handle.functionName(matlab.double([1, 2, 3]))
        handle.terminate()</pre></li>
        <li>Run the program: <pre>python driverApplication.py</pre></li>
        </ol>
        </body>
        </html>

        """.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Crlf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
}
