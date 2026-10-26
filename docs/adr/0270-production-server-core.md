# ADR 0270 — The Production Server core: mps.json, mcc -W CTF with discovery and routes files, compiler.build.productionServerArchive, and jgraph-mps answering MATLAB Production Server's synchronous requests

## Status

Accepted, 2026-10-26. Compiler and SDK plan, stage K14 (decisions KD1, KD8 and KO6).

## Context

MATLAB Production Server runs deployable archives built by `mcc -W CTF:<name> -U` (and
`compiler.build.productionServerArchive`) and answers HTTP requests for their functions:
`POST /<archive>/<function>` with a JSON body `{"rhs":[...],"nargout":n,"outputFormat":{"mode":"small"|"large","nanType":"string"|"object"}}`
and `{"lhs":[...]}` or `{"error":{...}}` back, in the JSON MATLAB's `mps.json.encode` and `decode`
write and read: a small notation (doubles and logicals nested by dimension, a char row as a string, a
scalar struct as an object with its fields in sorted order) and a large one (`mwtype`, `mwsize`,
`mwdata` column-major, `mwcomplex` and `mwsparse` when they apply, `mwmetadata` for a datetime). An
archive may carry a discovery file (function signatures the server's `GET /api/discovery` describes)
and a routes file (web handlers). Decision KO6 chose ASP.NET Core's Kestrel for the server; this
stage is the synchronous core, stage K15 the asynchronous requests, health, metrics and the admin
CLI, stage K16 protobuf.

What R2025b does was measured first by `record-k14.ps1` (`tools/matlab-checklist/compiler-probes`):

- **`probe_k14_build` (`k14_build`)**: over a copy of `src/k14`, the main build `k14arch` of 13
  functions with the discovery and routes files, 11 other `compiler.build.productionServerArchive`
  builds, one with no output folder, 69 `mcc -W CTF` forms (`DISCOVERY:`, `ROUTES:`, `-U`'s rules
  and every refusal), the options' 59 answers and refusals with 24 discovery files' faults, each
  archive's entries and manifest (`recordings/k14_out`, 59 files: discovery signatures live in the
  manifest as `<signature>` under each `<function>`, the routes file is copied unchanged to
  `fsroot/.META/.prodserver/routes/routes.json`).
- **`probe_k14_routes` (`k14_routes`)**: builds with routes files whose `component` names the
  archive (the main probe's named another), 18 shapes of the file and two served through the API.
- **`probe_k14_server` and `src/k14client/k14client.py` (`k14_server`)**: MATLAB Production
  Server's in-MATLAB test server (`serverSetup`/`serverStart` from `mps_in_matlab`), started under
  `matlab -r` and left idle at its prompt, twice: with CORS and discovery on, then off, exporting
  twelve of `src/k14`'s functions as `k14comp`; the Python client against it in three modes, 357
  cases over the two sessions: the request's shape, `nargout`, `outputFormat`, every class in and
  out, errors and their stacks, paths, methods, content types, preflights and origins, and the API's
  paths. `webwrite` from the same MATLAB cannot reach the server (it needs MATLAB idle), so the
  client is another process, and each MATLAB is ended by its own process id.
- **The fixtures** `k14_mps_json` (1,098 lines: the number format for doubles, singles and integers,
  both notations for every class, pretty printing, every decode refusal's identifier and message,
  `encoderequest` and `decoderesponse`) and `k14_production_server_archive` (127 lines: the options
  class, its set methods' checks, the build, and `mcc`'s forms). Four decode inputs end R2025b's
  process (a field name that is not a variable name, a string array element that is neither a string
  nor `{"mwdata":"missing"}`, a malformed sparse `mwdata`, an object `mwdata` under a class's
  `mwtype`); they were measured in processes of their own (`risky4`-`risky6`) and are not in the
  fixture.

Dispatch sweep 28 measured the four `mps.json` names and `compiler.build.productionServerArchive`
and its options class; `mps.json.decode` cannot be shadowed by a file (`which` keeps the built-in
method), so its row is the built-in under every pattern, which is what the sweep saw.

## Decision

**The codec, `JgsMpsJson`** (`src/JGraph.Scripting/Jgs/JgsMpsJson.cs`), writes and reads both
notations as measured: the small notation nests a double or logical array by its dimensions, first
outermost (a scalar bare, an N-by-1 flat, a 1-by-N `[[...]]`, 2-by-2-by-2 `[[[1,5],[3,7]],[[2,6],[4,8]]]`,
`[]` for a 0-by-0 double alone; a 1-by-0, a 0-by-3 and `logical([])` take the large form), a char
row as a string, a scalar struct as an object with its fields in ordinal order, NaN and the
infinities always as `{"mwdata":"NaN"}` objects there, and everything else in the large form with
its children small; the large form's keys alphabetical, data column-major, complex parts interleaved,
a sparse array as `[[rows],[cols],[values]]` from 0 (two lists when logical), a string array's
missing as `{"mwdata":"missing"}`, `struct([])` as `{}` in the small notation, an enumeration as its
members' names under its class, a datetime as `{"TimeStamp": ms since 1970}` with `Format` and
`TimeZone` metadata (the server's encoder adds the parts: `Day`, `Hour`, ...), and the unsupported
classes refused as `MPS:MATLAB:JSON:EncodeUnsupportedType` naming the class — a table in a cell by
its own name, a function handle or another object inside a container by the container's. Numbers
are `JgsJson.FormatDouble`'s (`%.15g` then `%.17g`; `1.0E+6` from a million up, `1E-5` below
`1e-4`); a single's digits are the fewest from six that read back, as a double within single's range,
to the same single (`realmax('single')` is `3.402823466E+38`, its nine-digit form lying past the
range). The reader refuses in R2025b's identifiers and words: syntax faults with the line, column and
character from 0 and a caret under the offending character; `DecodeArrayInconsistentTypes`,
`DecodeInvalidArrayElemType`, `DecodeMismatchedMWSize`, `DecodeNullNotSupported`, `DecodeInvalidMWType`
(with the foundation::json conversion text), each property's type, each numeric class's element
rule (integers from integer tokens, 64-bit saturating, an unsigned negative to 0, the others a range
error naming the type; a complex part a plain JSON number, R2025b refusing its own `"NaN"` word
there), `DecodeIncompleteComplexData`, char `["..."]` column-major keeping an empty's shape, string
and cell and struct counts, sparse ranges and dimensions, enumeration members, and a datetime's
`Format`, `TimeStamp` and `TimeZone`. The four inputs R2025b crashes on are refused in JGraph's words.

**The builtins** (`JgsBuiltins.Mps.cs`): `mps.json.encode(v, 'Format', 'small'|'large',
'NanInfType', 'string'|'object', 'PrettyPrint', tf)`, `decode(text)`, `encoderequest(rhs, 'Nargout', n,
'Format', ..., 'OutputFormat', ..., 'OutputNanInfType', ...)` — a cell `rhs` read as its first
`length(rhs)` elements in storage order, as R2025b reads it — and `decoderesponse(text)`, with
`inputParser`'s and `validatestring`'s words for the options, `MATLAB:TooManyInputs` for a second
argument to `decoderesponse`, and the char-matrix refusal `MustBeStringScalarOrCharacterVector`.

**The archive** (`src/JGraph.Deploy`): `FunctionSignatures` reads a discovery file (comments
skipped, `_schemaVersion` 1.0.0 or 1.1.0 — a missing one warned about, another refused —
`_typedefs`, each function's `inputs`, `outputs` and `purpose`, a type list with `size=r,c`, and
`struct:<typedef>`), checks it against the archive's functions in R2025b's order (every name in the
archive first, then each entry's shape and counts, a trailing `varargin` or `varargout` leaving the
count open) and in its words, a routes file's `version` string passed over and its `pathmap` array
taken for a function's name; `RoutesFile` reads a routes file in R2025b's two passes (each entry's
webhandler, the last entry first — its shape, then its `component`, which must be this archive's
name — then each entry's keys and `match`, a regular expression) and in its words; the manifest
carries each described function's `<help>` and `<signature>` (and JGraph's `<typedef>`),
`ProductionServerArchive.Write` writes `<name>.ctf` (target type 10, the routes file at
`fsroot/.META/.prodserver/routes/routes.json`) and a readme in JGraph's words. **mcc** reads
`-W CTF:<name>[,DISCOVERY:f][,ROUTES:f] -U` (the name letters, digits and underscores, the words in
any case; `-U` after `-m`, `-e` or another `-W` `COM_ERR_INVALID_SWITCH_LIB_OR_STANDALONE` while `-U`
before one leaves that wrapper the target with its archive separate, as stage K7 measured; `-W CTF` without `-U`
`COM_ERROR_MISSING_U_WITH_W_CTF`, `-R` `COM_ERR_INVALID_SWITCH_LIB_OR_COMP`, a second `DISCOVERY:`
`COM_ERR_MPS_MULTIPLE_DISCOVERY` while a second `ROUTES:` is passed over, the files looked for once
the line is read, a discovery file's fault `COM_ERROR_SIGNATURE_PARSE` and a routes file's
`COM_ERROR_ROUTES_PARSE` after its `COM_MESG_ROUTES_PARSE` warning, `-o` after the name
`COM_ERR_MULTIPLE_COMPONENT_NAMES`, `-U` alone naming the archive after the first file).
**`compiler.build.productionServerArchive` and `ProductionServerArchiveOptions`** are transcribed
from R2025b's `toolbox/compiler_sdk/cli/build`: `ArchiveName` from the first file,
`.\<name>productionServerArchive`, `FunctionSignatures` checked at its set under the label
`RoutesFile` (R2025b's own slip), `RoutesFile` only looked for, the hidden `getName`, and the build
through the same `mcc` words.

**The server, `jgraph-mps`** (`src/JGraph.ProductionServer`, published beside the engine in the
runtime's `bin\win64` and in JGraph's own folder; it runs on the ASP.NET Core shared framework):
`jgraph-mps new <folder> [--port]` lays out an instance (`config\main_config` in MATLAB Production
Server's `--option value` form: `--http`, `--num-workers`, `--auto-deploy-root`, `--log-root`,
`--request-timeout`, `--request-size-limit`, `--hide-matlab-error-stack`, `--enable-discovery`,
`--cors-allowed-origins`; `auto_deploy`, `log`, `pid`, `endpoint`), `run <folder>` serves it
(`endpoint\http`, `pid\main.pid`, `log\main.log`), `worker` is one worker. `ArchiveWatcher` deploys
every `.ctf` in `auto_deploy` and watches the folder; `WorkerPool` runs the workers, each this
program hosting `JgRuntime` with the archives open as components in sessions of their own, spoken to
over stdin and stdout in length-prefixed JSON frames, a request answered within `--request-timeout`
or cancelled and the worker restarted. `ServerHost` answers as the test server did: `200 OK` with
`{"lhs":[...]}` in the asked notation (large when none is asked) or `{"error":{"id","message","stack","type":"matlaberror"}}`,
Content-Length on every response; the refusals as reason phrases with an empty body and a
`(request id=0:N:0)` suffix — 400 `MissingOutputArgs nargout missing`, `MissingInputArgs rhs missing.`,
`InvalidInputArgs Invalid rhs.`, `InvalidInput Invalid input`, `InvalidJson Invalid JSON`,
`MissingContentType Content-Type header value missing.`, `Invalid HTTP`; 404 `ComponentNotFound`,
`FunctionNotFound`, `ResourceNotFound URI-path not of form '/APPLICATION/FUNCTION'`, and `URL not found <path>`
for any GET off the API; 405 `MethodNotAllowed Unsupported method`, `Bad Method`, `Method Not Allowed`,
`Access-Control-Request-Method not allowed`; 415 `UnsupportedContentType`, `InvalidContentType`;
500 `ReturnTypeNotSupported Function return type not supported.`; 403 `Discovery Disabled`,
`Metrics Disabled`, `Statistics Disabled`, `Archive Management Disabled`; a CORS preflight with
`Access-Control-Allow-Methods: POST, GET, DELETE` and the asked headers echoed, an allowed origin
echoed with `Vary: Origin` on every other response; `GET /api/discovery` in the recorded schema
(1.0.0, each archive's described functions with `help`, `mwsize`, `mwtype` and `name` per parameter,
`matlabRuntimeRelease` R2025b). `JGraph.Runtime.MpsCalls` runs a call on the component's engine
thread and encodes the outputs or the error. The routes file is read and carried but not yet routed
(stage K15; open item 211).

**Found on the way** (each the right answer, R2025b's): `sparse([])` faulted on an empty input
(now 0-by-0 with nothing stored); `isnumeric(true)` answered true (false: a logical is not
numeric); `isequal(struct(), struct([]))` answered true (false: a struct array compares by shape,
element by element); `complex(zeros(0, 2))` faulted (a scalar part spreads over the other's shape,
empty included); a single's JSON digits; a 1-by-1 cell's text in a property display was cut to 57
characters (R2025b shows an 85-character path whole); a char matrix given as an options class's
name is refused as `mustBeNonzeroLengthText` in `mustBeText`'s words; `-batch` reads a dotted
name from a file, not its statement (`run-k14.ps1` writes the build statement to one).

## Measured

- `run-k14.ps1` and `compare-k14.py` against R2025b's recordings: the build probe 317 of 422 keys
  agree, the rest recorded divergences; the routes probe 29 of 31, the other two the regular
  expression's words and the warning R2025b prints on standard output; the server 295 of 328 cases,
  33 recorded divergences, nothing unexplained; the server deploys an archive in 0.5-1.5 s.
- `ProductionServerArchiveTests` (4: the command line's forms and refusals, the discovery file's
  checks, the routes file's checks, the build's files and manifest), `ProductionServerTests` (1,
  needing CPython: the archive built in process, served by `ServerHost` on a free port, the probe's
  client run unchanged in both sessions, case for case with the recording less the divergences),
  and the fixtures `k14_mps_json` (1,098 lines from R2025b, 36 recorded divergences, both
  representations) and `k14_production_server_archive` (127, none).

## Divergences

- **The archive's readme is JGraph's words**, as every stage's generated prose is.
- **`-U` alone names the archive after the first file**; R2025b writes a nameless `.ctf`.
- **`-T link:lib` and `-T link:exe` with `-W CTF` build the archive alone**; R2025b fails the first for want of source files and builds a program beside the second.
- **`-R` with `-W CTF` is refused before the `-nojvm` warning**; R2025b warns first.
- **A discovery file's JSON fault is R2025b's `E0008` text when a value lacks its quotes, and .NET's words otherwise**; a routes file's JSON fault and a regular expression's fault are .NET's words.
- **`ObfuscateArchive` builds nothing**: JGraph builds no encrypted archives (decision K3b, ADR 0259).
- **A too-many-outputs call of `compiler.build.productionServerArchive` ends its message without R2025b's line break.**
- **A complex integer array and a complex sparse array are refused**, and a complex array that is empty or whose imaginary parts are all zero comes out real; JGraph holds none of them (ADR 0266).
- **`intmin('int64')`, `intmax('int64')` and `intmax('uint64')` encode as doubles, and an int64 or uint64 past 2^53 loses its last digits**, in the fixture and in the server's answers (ADR 0174, open item 199).
- **A sparse logical array is a sparse double** (ADR 0266).
- **A char array of three dimensions is refused** in JGraph's words; R2025b holds it.
- **A string in a struct field encodes as char** (open item 201).
- **`inputParser` is not a class JGraph has**; the fixture's stand-in, an `MException`, is refused by its own name.
- **`nargout('mps.json.decode')` answers 1**; R2025b refuses a built-in method's count.
- **The four inputs R2025b's decode ends its process on are refused** in JGraph's words.
- **A deployed error's stack has no `mpsExecuteInMatlab` frame**, and JGraph's words stand for too many outputs asked and an unassigned `varargout`.
- **`GET /api/health` answers 200 with no body** while the server listens; the test server answers 503 "Health check failed". The body is stage K15's.
- **The deployed function `k14quiet` answers `on`**: the in-MATLAB server inherits the probe's `beep('off')`; JGraph's worker is a fresh runtime.
- **A number printed with `%.17g` carries JGraph's own digits** (the client's `in.big.number` and `in.tiny.number`).
- **An options or results object displays in JGraph's layout** (open item 194), and **a warning prints without `> In` lines** (open item 170).

## Still open

- Item 211: the Production Server's leftovers — routing by the routes file, a worker's timeout and restart and more than one worker measured, the health body and the rest of the API (K15), protobuf (K16), the ASP.NET Core shared framework in the installers (K18), the beep.
- Item 212: `mps.json`'s leftovers — complex integers and sparse, int64 past 2^53, R2025b's crashes, a field name past `namelengthmax`, which classes name themselves inside a refused container, `mps.json.decode`'s dispatch row.
- Items 194, 170, 199 and 201, seen again.
