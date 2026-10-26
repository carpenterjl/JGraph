# ADR 0269 — Python packages: mcc -W python and compiler.build.pythonPackage, with the matlab module and the package runtime in Python over jgrt.dll

## Status

Accepted, 2026-10-25. Compiler and SDK plan, stage K13 (decisions KD1 and KO3).

## Context

R2025b's `mcc -W python:<name>` (and `compiler.build.pythonPackage`) builds a Python package. There is no
compiled code in it: a folder per part of the package's dotted name, the outer ones namespace packages
(`pkgutil.extend_path`), the innermost holding the deployable archive `<last part>.ctf` and the
`__init__.py` a program imports; `setup.py` and `pyproject.toml` to install it with pip; a readme; and,
from `compiler.build`, `GettingStarted.html`. The `__init__.py` checks the interpreter's version (3.9
to 3.12; a newer one is warned about and taken, an older one refused), finds the MATLAB Runtime by
`mclmcrrt25_2.dll` on the PATH, and imports two things from it: `matlab_pysdk.runtime`, pure Python over
the compiled `matlabruntimeforpython_abi3`, and the `matlab` module, whose array classes are a compiled
extension (`matlabmultidimarrayforpython`, built for 3.9, for 3.10 and as abi3). A program calls
`<pkg>.initialize()` and calls the package's functions as the methods of the handle it answers:
`h.fn(*args, nargout=1, stdout=, stderr=, background=)`, `async=` the deprecated name of `background=`,
a background call answering a `FutureResult`. Decision KO3 put a `matlab` module of the same names
inside the JGraph Runtime's own Python folder, as R2025b's lives inside its runtime, so client code
that imports `matlab` runs unchanged.

What R2025b does was measured first by `record-k13.ps1` (`tools/matlab-checklist/compiler-probes`):

- **`probe_k13_build` (`k13_build`)**: over copies of stage K9's functions (`src/k9`) and K13's
  (`src/k13`), the client's package `k13pkg` of 16 files (functions of every argument shape, a value
  class, a handle class, an enumeration, and `k13state`, a persistent counter), 16 other
  `compiler.build.pythonPackage` builds (the defaults, dotted names of three and two parts, a capital
  and an underscore name, each class alone, a package function, a namespace folder, `varargin` and
  `varargout`, an additional file, samples, a script, `class`, and `json`, a builtin module's name), a
  build with no output folder, 25 `mcc -W python` forms, and the options' defaults, derived names and
  refusals, with every file each build wrote and its text (`recordings/k13_out`).
- **`k13client.py` (`k13_client`)**, a Python client of `k13pkg`, in eight modes: `arrays` (335
  cases of the `matlab` module alone: constructors, attributes, methods, indexing, slicing, buffers,
  comparisons, copying, pickling), `calls` (84: names, keywords, outputs, the streams, errors,
  objects), `convert` (202: values out of `k9make`, into `k9describe` and back through `k9echo`),
  `background` (48), `life` (26: a second instance, `terminate`, `exit`, `quit`,
  `terminate_runtime`), `extra` (79: datetimes, durations, dictionaries, ranges, an error's `str`
  and `repr`), `runtime CASE` (`initialize_runtime`'s ten cases, a process each) and `version M N`
  (the package imported as if Python were 3.8, 3.9, 3.13, 3.14, 4.0 or 2.7).

What it found, beyond the documented surface:

- **The array classes share no base**: `matlab.double.__mro__` is `(double, object)`; indexing is
  0-based, a slice is a view onto its array, `reshape` works in place, `toarray()` takes a 1-D
  sequence only, `__hash__` is None and ordering is refused; on 3.12 an array is a buffer of its
  elements in column-major order, read-only.
- **A package name may be almost anything**: `1bad`, `a-b`, `class` and `json` are written as given,
  though Python cannot import the first three and the last shadows the standard module; empty dotted
  parts are dropped, a second `-W python:` nests its name inside the first's, `version=` is warned
  about and ignored, `-C`, `-T` and `-g` change nothing, `-c` writes a C library's source, and `-o`
  beside `-W` is refused either way round.
- **R2025b's own failures**: deleting an element of an array ends the process (0xC0000005);
  `initialize()` after `terminate_runtime()` never returns; `toarray()` of a 2-D array prints the
  `TypeError` it meets to standard error before refusing; a `range` passed in is taken for a
  dictionary and fails; `initialize_runtime(None)` raises `SystemError` from the extension; a nested
  empty cell comes back as `matlab.double([])`, where an empty cell alone comes back as `[]`.

## Decision

- **The `matlab` module and the package runtime are JGraph's own Python, under MathWorks' names
  inside the runtime** (KO3, `python/jgraph_runtime` in the repo, `<runtime>\python` when published):
  `matlab` (the eleven array classes made by one class factory, each with `__slots__` and R2025b's
  methods, properties, messages and buffer layout; `matlab.object` for a MATLAB object held by the
  engine; `matlab.dictionary`), `mlexceptions` (`ShapeError`, `SizeError`) and `matlab_pysdk.runtime`
  (`DeployablePackage`, `DeployableFunc`, `FutureResult`, `MatlabRuntimeError` and the five other
  exceptions, R2025b's checks in R2025b's order and its catalog's words). Nothing is compiled, so one
  copy serves every CPython from 3.9 on.
- **The runtime reaches the engine through jgrt.dll** (`_jgrt.py`, ctypes): stage K12's
  `jgrtCppStart`, `Stop`, `Close`, `WaitForFigures`, `Call`, `Cancel`, `Forget`, `Release` and `Free`,
  and a new `jgrtPyOpen`, which opens an archive in a session of its own and marks the instance as
  Python's, so `NativeWire` carries a datetime, a duration and a dictionary across as values (forms 100
  to 102: a datetime's wall-clock milliseconds, whether it has a zone and its offset; a duration's
  milliseconds; a dictionary's keys and values). Arrays cross in `NativeWire`'s form both ways; a
  call's text reaches the call's own `stdout` and `stderr` objects, or the process's streams.
- **A session per library opened**: `Component.Open(path, ownSession: true)` gives each open its own
  session, keyed `uuid#n`, ended when the component is disposed. jgrtCppOpen opens the same way, so
  stage K12's C++ libraries now run each in a session of its own as R2025b's do (ADR 0268 amended).
- **The runtime starts at the first `initialize()`**, as R2025b's: `initialize_runtime` stores its
  options, and an option it does not know is warned about then. A queued call cancelled through its
  `FutureResult` completes at once, so `done()` answers True straight after `cancel()`, for the C++
  `FutureResult` too.
- **Buffers**: on 3.12 `__buffer__` answers a read-only memoryview made by
  `PyMemoryView_FromBuffer` over the array's storage, strided column-major as R2025b's, its owner kept
  alive by the view; before 3.12, where a Python class cannot export a buffer, `numpy.asarray` reads
  `__array_interface__` and `tomemoryview()` answers a copy laid out by rows.
- **`JGraph.Deploy.PythonPackage`** writes the archive, the namespace levels, the package's
  `__init__.py`, `setup.py`, `pyproject.toml` and `readme.txt` with CR LF, and `GettingStarted.html`
  with line feeds, as R2025b's. The `__init__.py` is JGraph's: R2025b's class constants
  (`SUPPORTED_PYTHON_VERSIONS`, `RUNTIME_VERSION_W_DOTS`, `PACKAGE_NAME`), module functions
  (`initialize`, `initialize_runtime`, `terminate_runtime`, the `atexit` hook) and version checks
  with their messages, over a search for the JGraph Runtime of the building JGraph's version:
  `JGRAPH_RUNTIME_ROOT`, then the registry key `SOFTWARE\JGraph\JGraph Runtime\<version>`
  (`Location`, machine then user), then each PATH folder holding `jgrt.dll`. A runtime's root is
  known by its marker `jgraph-runtime.json`, which must name the same version (a runtime of another
  version is passed over and named if nothing else is found); a folder holding the engine, jgrt.dll
  beside it and `python\matlab_pysdk`, as JGraph's own does, is taken as it is.
- **`MccCommandLine`** reads `-W python:<name>[,version=v]` (`MccTarget.PythonPackage`) with the
  forms and refusals above; **mcc** builds the package from every file named;
  **`compiler.build.pythonPackage`** and **`compiler.build.PythonPackageOptions`** are transcribed
  from R2025b's `toolbox/compiler_sdk/cli/build` (pythonPackage.m, PythonPackageOptions.m, the
  builder and PythonWriter.m): `example.<first file>` and `.\<last part>pythonPackage` when not given,
  the hidden `getName`, the checks of every property, the mcc command the writer makes, and
  `GettingStarted.html` added to the results' files.
- **The runtime carries the Python folder**: `publish-runtime.ps1` copies it into `<root>\python` and
  checks for it; JGraph's own output carries it beside the engine (`python\`), which the installer's
  staging checks.
- **Met on the way:**
  - **A fixed offset written with seconds of none** (`+02:00:00`), the form a datetime from Python
    crosses in, is read as its offset (`JgsTime.TryReadFixedOffset`). A probe of 42 spellings found
    R2025b's rule wider: it reads seconds, digits alone and `UTC+2`, takes offsets to 23:59, names
    each `+HH:mm`, and refuses the rest as `MATLAB:datetime:UnknownTimeZone` (open item 209).
  - **A cell given for a text option** is refused without "Instead its type was cell.", as
    R2025b's `validateattributes` words it.
  - **Dispatch sweep 27** for the two new names, with their output counts (neither has a fixed one).

## Measured

- **`run-k13.ps1`** runs the same probe under `jgraph -batch` and the same client, unchanged, against
  JGraph's package on a stand-in runtime first on the PATH with R2025b's off it; `compare-k13.py`
  writes `recordings-jgraph/k13_differences.txt`, every difference put down to a divergence below or
  an open item, nothing unexplained:
  - The builds: 150 of 300 keys agree. The others: the generated files in JGraph's words (96), an
    object's display (34, item 194), the runtime listed as one product (16, ADR 0267), the version
    line, and no samples (item 207). The folders, the files' names, the archive's place, the namespace
    levels' code, the package's constants and functions, `setup.py`'s distribution name, version,
    packages and package data, and `pyproject.toml`'s build system are R2025b's.
  - The client: 798 of 835 cases and error reports agree; 38 others are recorded. The `arrays` mode
    agrees on 334 of its 335 cases. The rest: JGraph's storage limits (64-bit integers past 2^53,
    complex integers and complex sparse arrays, a zero imaginary part; ADR 0266), the corrections and
    differences below, a value's display and a deployed warning's location line (items 194 and 170),
    an operator's name heading its error (item 170) and an undefined function's words (item 203).
  - Builds take 0.0 to 0.3 s (the main build 0.3 s); R2025b's take 9.1 to 131.1 s (the main build
    131.1 s, the others mostly 10 to 14 s). `initialize()` takes 0.8 to 1.8 s; R2025b's 2.8 to 12.7 s.
- **The plan's fixture**: the research session's client (`research-2026-10-21/mcc_probe/clients/py_client.py`),
  unchanged, against JGraph's build of its `jgprobe` on a runtime published by `publish-runtime.ps1`
  and found through `JGRAPH_RUNTIME_ROOT`: every answer R2025b's, its values displayed in JGraph's layout
  (item 194); `initialize()` 1.9 s against R2025b's 56.1 s.
- **Tests.** `PythonPackageTests` (4): the command line's forms and refusals, the dotted name's
  folders, the files a build writes with their line ends, and the lines a program or pip depends on
  against `k13_out`. `PythonPackageBuildTests` (1, skipped where no CPython is found): the package
  built in process by `compiler.build.pythonPackage`, the client run in its `arrays`, `calls`,
  `convert` and `extra` modes with `JGRAPH_RUNTIME_ROOT` naming the test's folder, case for case with
  R2025b's run. The fixture `k13_python_package` (74 lines from R2025b, none diverging, both
  representations): the options' defaults, derived names and refusals, each property's checks, 17
  mcc forms with what each writes and refuses, the generated lines read back, and the results.

## Divergences

- **The package's `__init__.py`, `setup.py`, `pyproject.toml`, readme and getting-started page are JGraph's words**, for the JGraph Runtime: the package finds the JGraph Runtime of its version (`JGRAPH_RUNTIME_ROOT`, the registry, the PATH) where R2025b's finds the MATLAB Runtime on the PATH, and `setup.py` names no author or URL, gives the platform as Windows alone and takes setuptools without R2025b's distutils fallback and clean-up; R2025b's names, constants, functions, messages, distribution name, version and packages are kept.
- **A dotted package's readme names its first part's folder and its whole name to import** (`import org.k13.deep`); R2025b's names the last part (`deep`), which is neither the folder beside `setup.py` nor importable.
- **The `matlab` module and the package runtime are JGraph's own Python** under MathWorks' names inside the runtime (KO3), reaching the engine through jgrt.dll; R2025b's are compiled extensions over its runtime.
- **An array's unhashable error names its class without its module** (`unhashable type: 'double'`): its `__hash__` is None, as R2025b's is, and Python words the refusal; R2025b's extension names `matlab.double`.
- **Deleting an element of an array raises the error deleting a row raises** (`RuntimeError: null data pointer.`); R2025b's process ends.
- **`toarray()` of an array of two dimensions refuses without printing**; R2025b's prints the `TypeError` it meets to standard error first.
- **A nested empty cell comes back as `[]`**, as an empty cell alone does; R2025b's gives `matlab.double([])` inside a cell.
- **A `range` passed in is a sequence**, a cell row of int64 as a list or tuple of ints is; R2025b's takes it for a dictionary and fails.
- **`initialize_runtime(None)` starts the runtime with no options**, as an empty list does; R2025b's raises `SystemError` from its extension.
- **After `terminate_runtime()`, a package is refused at once in JGraph's words** (`SystemError`: the runtime has ended in this process and cannot start again); R2025b's `initialize()` never returns in a process that terminated before starting, and says "MclJmiUtils plugin not available" in one that had started.
- **A datetime made from Python keeps its time zone as char**, as JGraph's datetimes do; R2025b's keeps the string it was made with.
- **A datetime keeps its moment to about half a microsecond near 2026** (milliseconds since 1899 in a double), so a Python datetime's microseconds may come back one off; R2025b's keeps them.
- **A duration keeps a timedelta's microseconds**; R2025b's drops what is finer than a millisecond.
- **Before Python 3.12 an array exports no buffer**: `memoryview(a)` is refused, numpy reads the array through `__array_interface__`, and `tomemoryview()` answers a copy laid out by rows (a complex array's refused); R2025b's compiled arrays export one on every version.
- **`FutureResult`'s private attributes are JGraph's own**; its public ones are R2025b's.

## Still open

- Item 210: the Python package's leftovers: no sample drivers are written from `SampleGenerationFiles` (R2025b writes `samples\k13sample1.py` and `k13sample2.py` for `k13sample.m`); only CPython 3.12 was run, so the path before 3.12 (`__array_interface__`, `tomemoryview()`'s copy) and the version checks were reached by replacing `sys.version_info`; neither engine's package was installed with pip; a package's figures were not probed; and a `matlab.object` handed to another package is refused, unprobed in R2025b.
- Item 209: fixed-offset time zones as R2025b reads and names them.
- Item 194, seen again: a deployed value's display is JGraph's own layout. Item 170, seen again: no "> In" line after a deployed warning, and no "Error using *" heading an operator's error. Item 203, seen again: an undefined function's words in a deployed report.
