# ADR 0258 — The archive, the runtime cache, `extractCTF`, `zip` and `unzip`

## Status

Accepted, 2026-10-23. Compiler and SDK plan, stage K3.

## Context

Every compiler target carries a program's files in an archive: R2025b's `.ctf`, appended to a
standalone `.exe` or written beside a library. At run time the MATLAB Runtime extracts it into a
cache, and `ctfroot` answers the extracted copy. Stage K2 decided which files go in; this stage
packs them, unpacks them, and runs a program from the unpacked copy.

What R2025b does was measured before any code was written:

- **The archive.** The research session's builds (`docs/plans/notes/compiler-sdk-research-2026-10-21/mcc_probe/artefacts`)
  were re-read, together with K2's `k2_mcc.out.txt` listings.
  - An archive is an OPC package (a ZIP): `[Content_Types].xml`, `_rels/.rels`, a signature under
    `_xmlsignatures/`, `.META/manifest.xml`, five `metadata/*.xml` parts, and the files under `fsroot/`.
  - Every entry is dated 1 January 1980.
  - A file under the main's folder sits under `fsroot/<component>/`, and any other file under its
    full path without the drive.
  - A standalone `.exe` carries the ZIP after its last section. The ZIP's offsets are relative to
    where it starts, and its end record is found from the end of the file.
  - The manifest's `target_type` numbers are 1 for standalone, 2 for windowed, 3 for C, 7 for .NET,
    9 for C++, 10 for the server and 14 for Python. `mcc -n` sets `use_native_matlab_types="1"`.
- **`extractCTF.exe`**, run through `run-quiet.ps1`:
  - With no folder it extracts to `<name>_mcr` in the current folder; a folder given takes the
    files directly, and further arguments are ignored.
  - It prints `<arg> CTF file extracted in the current working directory` (or `in <folder>`) on
    standard error.
  - It refuses with `Could not open the CTF file <arg>`, adding no extension, and exit code −1.
  - Run with no arguments it prints its usage on standard output and `No CTF file specified at the
    input. ` on standard error.
  - It reads an `.exe` as well as a `.ctf`.
- **The runtime cache.** `record-k3-cache.ps1` ran K0's programs under a fresh `MCR_CACHE_ROOT` per
  case and recorded 22 cases in `recordings/k3_cache.out.txt`. The cache's own state on this
  machine was read too.
  - The root holds `.deploy_lock`, `.max_size` (bytes) and `.mcr_cache_index`: four CR LF lines
    per entry, giving the uuid, the folder, a size, and the last use (`2026-Oct-24 01:35:35.463113`, UTC).
  - An entry's folder is the uuid's first six characters and a number (`k0_env0`, `hello_1`).
    The folder holds `.deploy_lock` and `.created.ts`.
  - A missing `.created.ts` or entry folder means the archive is extracted again into the same
    folder. A missing index means a new folder, and the old one is listed with an empty uuid.
  - Over the limit, the least recently used other entries are deleted.
  - A deleted cached file stays deleted (the function is undefined). A changed one fails with
    `MATLAB:err_while_looking_up_function`.
  - `MCR_CACHE_SIZE` is written as its leading digits: `150M` as 150, `abc` as 0. The limit then
    counts that number as bytes (`1000K` evicted what 400000 kept).
  - `.max_size` is written only when the cache is made.
- **`zip` and `unzip`** are `.m` files in R2025b (`toolbox/matlab/io/archive`) and were transcribed
  from their source. The fixture `k3_zip_unzip` records 90 lines of their answers and refusals.

## Decision

### The archive (`JGraph.Deploy`)

- **`CtfArchiveWriter`** writes R2025b's package layout, and decision 7.2 of the plan holds: the
  format is JGraph's own.
  - There is no signature, and the content type is `application/vnd.jgraph.compiler` ("JGraph
    Compiler Archive").
  - The parts are R2025b's, in its order, dated 1 January 1980, so the same inputs make the same
    bytes except for the uuid and the creation time.
  - The XML declarations are written as R2025b writes them (`encoding="UTF-8" ... ?>`).
  - A place that leaves the file root (`..`, a drive, a rooted name), and two files in one place,
    are refused.
- **`CtfManifest`** keeps R2025b's element structure:
  - the `manifest` root with `component-name`, `uuid` (`<component>_<guid>`), `target_type`, the
    runtime version 25.2.0 (KO2) and `use_native_matlab_types`;
  - a `directory` per path folder, with ids made unique when two would collide;
  - a `function` per public function;
  - a `configuration` with the path, the runtime options, the warning state
    (`off:MATLAB:dispatcher:nameConflict`), `prefdir` (`.matlab`), the main function and the public
    functions.

  R2025b's keys, signature and licence attributes are left out. JGraph adds `jgraph-version`,
  `protection` (only `open` until K3b), each function's `file`, and a `file` element per file with
  its length and SHA-256.
- **`CtfLayout`** places files by R2025b's rule. `FromReport` turns K2's report into a build: the
  main's folder first on the path, then each other folder that holds a taken file. A `private`,
  `@`, `+` or dot folder is never on the path.
- **`CtfArchiveReader`** opens a `.ctf`, or a program carrying one, by finding where the ZIP starts.
  - It refuses a MathWorks archive by name. Its signature part or its content type gives it away,
    and its code is encrypted with MathWorks' keys.
  - It checks each file's length and hash as it reads.
  - It extracts into a folder, with the `.matlab` preferences folder, and never writes outside it.

### The runtime cache (`RuntimeCache`)

- It keeps R2025b's files, names and rules, as measured. The folder is still `mcrcachedir`:
  `%LOCALAPPDATA%\JGraph\RuntimeCache\<JGraph version>`, or `MCR_CACHE_ROOT\<version>` (K1).
- **Locks.** The root's `.deploy_lock` is held while the index changes and an archive is
  extracted, so concurrent starts share one extraction. An entry's `.deploy_lock` stays open while
  its program runs, so another start never evicts it. `.created.ts` is written last.
- **The tamper check.** JGraph also writes `.jgraph_extracted`: each file's length and write time.
  A start checks the copy against it and extracts again on any difference.

### Running an archive

- **`ScriptDeployment.FromArchive`** extracts through the cache. `ctfroot` is the entry's folder
  and `mcrcachedir` the cache's.
  - The frozen path and the main function come from the manifest, and `mcc -n` from
    `use_native_matlab_types`.
  - The entry is held until the deployment is disposed.
- **`jgraph --deployed <archive> -- args`**, without `-batch`, runs it as a compiled program runs.
  This is the run path K6's launcher calls into. `--deployed-numeric` is refused there, since the
  archive says whether it was built with `-n`. The K1 form, `-batch main.m --deployed <folder>`, is
  unchanged.
- **`jgraph -extractCTF <archive> [<folder>]`** is R2025b's utility with its messages and exit codes.

### `zip` and `unzip`

They are transcribed from R2025b's `zip.m`, `unzip.m` and `getArchiveEntries.m`.

- **Validation.**
  - The arguments block's validators are R2025b's (`mustBeTextScalar`, `mustBeText`,
    `mustBeNonzeroLengthText`), with their positions.
  - `MATLAB:minrhs`, the positional-count refusal and `nargoutchk` are R2025b's.
- **`zip`'s entries.**
  - The entries come from a queue of the names given, repeats dropped. A folder's contents are
    queued behind everything already there, in ASCII order (`Q.txt` before `m.txt`). An empty
    folder is an entry, and a wildcard in the last part of a name is expanded.
  - An entry is named relative to the root folder. An absolute name keeps only its file name, and
    a `../` name is kept as written.
  - `.zip` is added unless the name ends with it, and a folder name is refused.
  - Duplicate entries are `MATLAB:zip:duplicateEntry`, and the archive inside its own input is a
    `MATLAB:zip:archiveName` warning.
- **`unzip`.**
  - It writes into the folder given, making it when missing, or into the current folder for `.`
    or `''`, replacing files that are already there.
  - It answers each name under the folder as given: a bare name for the current folder, and a
    folder's name ending in `\`.
  - It looks for the name as given, then with `.zip` or `.ZIP` added, in R2025b's refusals.

## Consequences

- K6's `mcc` builds with `CtfLayout.FromReport` and `CtfArchiveWriter`. It writes K2's logs into
  `.META/` through `CtfBuild.MetaFiles`, and appends the archive to the launcher, which the reader
  already finds. `mcc -C`'s copy beside the program (`<exedir>\<name>_mcr`) is `ExtractTo`.
- `compiler.build.Results.BuildID` is the manifest's uuid (K6).
- K3b adds protection levels as values of the manifest's `protection` attribute. A runtime that
  meets a level it does not know refuses the archive and says it is newer.
- K4 puts `extractCTF.exe` in the runtime's `bin`, over `ExtractCtfCommand`.

## Measured

- **The archive.** `CtfArchiveTests` (10) cover:
  - the layout against R2025b's listings and the package's parts and dates;
  - the manifest's structure and round trip;
  - extraction;
  - an archive after 123,457 bytes of program;
  - a MathWorks archive and a damaged file refused;
  - `extractCTF`'s messages and exit codes as R2025b's utility printed them.
- **The cache.** `RuntimeCacheTests` (21) check each measured rule against `k3_cache.out.txt`
  (naming, reuse, the marker, a missing folder or index, eviction, a held entry) and the four
  divergences below.
- **End to end.** `DeployedSessionTests` gains five cases. An archive runs from the cache's copy,
  with `ctfroot`, `mcrcachedir`, `which` and a data file on the frozen path. A second run reuses
  the copy, an uncaught error exits with 249, a file that is no archive is refused, and the command
  line is checked.
- **The fixture** `k3_zip_unzip` prints R2025b's 90 lines here, one of them a recorded divergence,
  in both representations.
- **The sweep.** Dispatch sweep 20 found both names going to a shadowing file under every pattern,
  as `.m` files do. Their `nargout` is −1, so they have no output-count row.

## Divergences

- **The archive is JGraph's own.** It has no signature and JGraph's content type. JGraph refuses
  an archive built by MATLAB Compiler, and R2025b will not run JGraph's.
- **The manifest leaves out R2025b's keys, signature and licence attributes.** It adds the JGraph
  version, the protection level, each function's file and each file's hash.
- **Until K3b the code is stored as written.** R2025b encrypts every `.m` (`V2MCC`).
- **`MCR_CACHE_SIZE`'s unit counts.** R2025b writes `150M` as 150 and evicts as though it were 150
  bytes, so JGraph reads `150M` as 150 MB, as MathWorks documents. A value that is not a size means
  the default, where R2025b gives 0, and the variable is honoured on every start, where R2025b
  honours it only when the cache is made.
- **A cached file that was deleted or changed is extracted again.** R2025b runs without the file,
  or fails on it.
- **The index's size column is bytes on disk.** R2025b's number is neither the folder's bytes nor
  the archive's.
- **`unzip` refuses an entry that would land outside its folder**, and so writes nothing.
  R2025b writes `../outside.txt` above the output folder (`k3_zip_unzip`'s `unzip_dotdot`). The
  refusal is `JGraph:unzip:entryOutsideFolder`.
- **No encryption in `zip` or `unzip`.** `zip` refuses `Password` and `EncryptionMethod`
  (`JGraph:zip:encryptionNotSupported`). `unzip` accepts a `Password` for an archive that is not
  encrypted, as R2025b does, and refuses an encrypted entry.
- **`unzip` does not download a URL** (`JGraph:unzip:urlNotSupported`). R2025b fetches http,
  https, ftp and file URLs first.
- **`extractCTF` is `jgraph -extractCTF`** until K4 ships the utility. For a file that is an
  archive it cannot read, it adds a line saying why.
- **The path puts the main's folder first.** R2025b put an `-a` folder before the main's (K0's
  `env_plain`); K6 measures `mcc`'s order.

## Still open

- K3b: the protection levels and decision KO1.
- K4: `extractCTF.exe` in the runtime layout. K6: `mcc`, the `-C` copy, `Results.BuildID`, and the
  path order with `-a`.
- R2025b's empty `<component>_<uuid>` folder in an extracted copy, and the `.bytes_used` it writes
  in an orphaned one, are not written. Nothing was seen to read either.
- `prefdir` under `ctfroot\.matlab` stays with open item 169.
