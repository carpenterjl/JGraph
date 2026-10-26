# ADR 0271 — The Production Server's asynchronous requests, web handlers, health, metrics and admin commands, and MATLAB Production Server's metrics and persistence helpers

## Status

Accepted, 2026-10-26. Compiler and SDK plan, stage K15 (decisions KD8 and KO6).

## Context

Stage K14 (ADR 0270) built `jgraph-mps`, the JGraph Production Server, answering MATLAB Production
Server's synchronous requests as R2025b's in-MATLAB test server answered them. What that server
lacks, stage K15 adds: the asynchronous request protocol of the RESTful API (`?mode=async`, the
request's states and sequence numbers, its representation, collection, state, result, cancel and
delete), the web handlers a routes file maps paths to, the health check's body, the metrics API in
Prometheus text, an admin command line shaped like MPS's `mps-*` commands, and the helpers deployed
code calls: `prodserver.metrics.incrementCounter` and `setGauge`, `mps.sync.mutex` and the
`mps.cache` persistence cache over its MAT-file provider.

**The oracle changed.** R2025b ships `muserve.exe` in `bin\win64`, the one-archive Production Server
its `matlabtest` fixture `StartMuserveServer` runs: MPS's own libraries (`mps_master.dll`,
`mps_worker_app.exe`) serving one archive with `--http`, `--enable-discovery` and `--enable-metrics`
(both on by default), `--cors-allowed-origins`, `--routes-file`, `--request-size-limit`,
`--hide-matlab-error-stack`, `--user-data`, `--pid-root` and `--endpoint-root`. `record-k15.ps1`
builds `k15comp.ctf` with R2025b's `mcc -W CTF` from `src/k14` and `src/k15` (a discovery file and
an archive routes file) and serves it with muserve twice, the client `src/k15client/k15client.py`
(k14client's modes plus `async`, `routes`, `metrics`, `env`, `size`, `hidden` and `disabled`) run
against each: a full session (CORS, an instance routes file, user data) and a limited one (discovery
and metrics off, a 1 KB request limit, the stack hidden). Measured:

- **muserve agrees with the in-MATLAB test server on every one of stage K14's 290 cases but two**:
  `GET /api/health` is `200 {"status":"ok"}` (the test server's 503 "Health check failed" was its
  own), and the archive's uuid in the discovery is `<name>_<uuid>` where the test server wrote none.
- **It has no asynchronous requests**: `?mode=async` is `400 mode=async is not supported.`, and a
  path under `/~…/requests` is `404 URL not found`. The asynchronous protocol is therefore built from
  the RESTful API's documentation (POST Asynchronous Request, GET Representation, GET Collection of
  Requests, GET State Information, GET Result of Request, POST Cancel Request, DELETE Request), whose
  paths, fields, states and status codes are quoted in `ServerHost` and `AsyncRequests`.
- **Web handlers** (`route.*`, 78 cases): the handler gets one struct, `ApiVersion` [1 0 0], `Method`,
  `Path` (the target as sent, query included), `HttpVersion` [1 1], `Headers` an N-by-2 cell of the
  request headers in wire order, `Body` a 1-by-N uint8; every method but PATCH reaches it (PATCH is
  `400 Invalid HTTP` before routing), a HEAD runs it and answers the body's length alone; `match` is
  a regular expression matched anywhere in the target unless anchored, case as written, in the
  file's order; an entry naming a function the archive lacks is `500 invalid work type`, one naming
  an archive not deployed `404 component not found` (followed by a C++ function's name muserve
  leaks); the routes an archive carries apply only while the instance has a routes file of its own
  (none of them answered in the limited session), to targets whose first segment names the archive,
  matched after it (`/k15comp/arch/web` against `^/arch/web$`), the instance's entries first. The
  answer struct is checked field by field: exactly one 1-by-1 struct output (`WebFunctions must
  return exactly one output (1x1 struct)`), no field but the five (`Invalid response struct (invalid
  field "Extra")`), `Body` a 1-by-N uint8 (a 0-by-0, a char, a double or a column refused), `HttpCode`
  a 1-by-1 double (an int32 or text refused, 200.5 is 200, 599 passes), `HttpMessage` a non-empty
  char row (a string refused), `Headers` an N-by-2 cell with N at least 1 (a 1-by-4, a 0-by-0 or a
  struct refused; a numeric value dropped); a field left out is 200, `OK`, no headers, no body; a
  handler that fails is `500 MATLAB error: <message>`; these 500s carry no request id.
- **Metrics** (`metrics.*`): ten server series in a fixed order, then the custom ones a function
  emitted by printing `[metric:counter] <archive>|<name> <value>` or `[metric:gauge] …`, read off the
  worker's standard output and its standard error alike, leading blanks allowed, nothing before the
  bracket, a value that is not a number dropped, a name with dashes kept, a type word that is neither
  written as `untyped`; a counter adds and a gauge sets; a gauge event on a counter's name is passed
  over; the series without an archive come first, then each archive's in the order first seen,
  names in ordinal order; values as C's `%g` (1.23457e+08, 1e+06, inf, nan). The archive label is
  `ctfroot`'s folder, `k15comp_1`, MPS's `<name>_<n>`. With metrics off, `403 Metrics Disabled`
  after a HEAD's `405 Bad Method`.
- **The environment** (`env`): `isdeployed` true, `ctfroot` ending in `k15comp_1`, `mcrversion` 25,
  `getmcruserdata` answering `--user-data`'s value, `beep` on when the worker starts (`quiet` answers
  `on`, so stage K14's divergence on it is retired), the `nameConflict` warning off.
- **Limits**: a `Content-Length` past `--request-size-limit` is `413 Content-Length exceeds size
  limit of 1024 bytes` before anything else, routed requests included; `--hide-matlab-error-stack`
  leaves the `stack` key out of an error reply altogether; `--worker-restart-interval` muserve
  refuses in every spelling ("bad lexical cast"), so a worker's restart stays unmeasured.

The in-MATLAB helpers were measured by the fixture `k15_mps_cache` from R2025b's M-files
(toolbox/mps/metrics and toolbox/mps/persistence): `prodserver.metrics`' event line and every
refusal in validateattributes' and the validators' words; `mps.cache.control` over the MatlabTest
provider (a MAT-file service in a folder: `.configFile.mat`, `<cache>.mat` with `name` and `value`,
`#compositeKey_<cache>.mat`), the Controller's start, stop, attach, detach, restart, ping and
version with every state's refusal, `mps.cache.connect`'s DataCache (dot access, put, get, keys,
isKey, remove, clear, length, bytes, retain, flush, purge, getp, composite struct keys by a
fingerprint), `mps.cache.Connection`'s list, exist and clear, and `mps.sync.mutex`'s acquire, release
and own with an expiring lease. R2025b lists a `dynamicprops` Controller's properties in an order
that changes from run to run; the fixture sorts them.

## Decision

1. **`jgraph-mps` answers the whole interface.** `ServerHost` routes in muserve's order: the API's
   exact paths; a `Content-Length` past the limit; a method HTTP has not; the asynchronous paths
   `/~<instance>/requests…`; a routes file's match; then stage K14's synchronous rules. `GET
   /api/health` answers `{"status":"ok"}` while the server listens (MPS's 503 is for a lost license
   server, which JGraph has none of). `GET /api/metrics` answers `MetricsRegistry`'s text with
   `--enable-metrics`, the ten server series (up time, the last request's queue wait, CPU time, the
   working set, requests accepted, in queue, processing, succeeded, failed and cancelled) and the
   custom series read off every line the workers print.
2. **Web handlers.** `RouteTable` holds the instance's routes file (`--routes-file`, relative to the
   instance's folder; a bad one stops the server as it stops MPS) and each deployed archive's own,
   read from the manifest's `.META/.prodserver/routes/routes.json`. A matched request goes to the
   worker as a `web` frame (method, target, version, headers, body base64); `MpsCalls.WebAsync`
   builds the request struct, calls the handler with one output and checks the answer as muserve
   does, in its words; the server writes the handler's code, message, headers and body, a
   `Content-Length` the handler wrote dropped. The headers reach the handler as Kestrel keeps them,
   Host first.
3. **Asynchronous requests** (`AsyncRequests`): `?mode=async` (that spelling alone; `client=` kept
   as the request's attribute) answers `201 Created` with `Location` and `X-MPS-Start-Time` and the
   representation `{"id","self","up","lastModifiedSeq","state":"READING","client"}`, `self` being
   `/~<instance uuid>/requests/<request uuid>`; the request then goes IN_QUEUE, PROCESSING when a
   worker takes it, READY with its reply (a MATLAB error included, as the Java client reads it) or
   ERROR with the server's refusal; `GET self` the representation, `GET self/info`
   `{"request","lastModifiedSeq","state"}`, `GET self/result` the reply, `409 RequestNotCompleted`
   before, `410 RequestAlreadyCancelled` or `RequestAlreadyDeleted` after; `POST self/cancel` 204
   or `410 RequestAlreadyCompleted/Cancelled/Deleted`, a running function stopped at its next
   statement and its worker kept; `DELETE self` 204 for a finished request, `409 RequestNotCompleted`
   for a running one; `GET up?since=N&clients=a,b|ids=…` the requests changed after N, latest first,
   waiting for a change when none has, with `400 MissingParamSince`, `InvalidParamSince`,
   `MissingQueryParams` and `NoMatchForQueryParams` and `404 ClientNotFound`; a path under another
   instance id `404 URL not found`. Every change stamps the server's next sequence number; a
   deleted request is remembered for its 410s, ten thousand at most.
4. **Admin commands**, shaped like MPS's: `jgraph-mps start [-C] <folder> [-f]` runs the instance
   detached and prints `'<folder>' STARTED` with its address once `endpoint\http` and `pid\main.pid`
   are written; `stop [-f] [-k] [--timeout hh:mm:ss]` asks it to end through a named event and
   waits; `restart`; `status` prints `'<folder>' STARTED` and the address, or `STOPPED` (a stale pid
   counts as stopped); `check host:port` asks `/api/health` and prints `Check completed
   successfully`; `deploy <folder> <archive>…` and `undeploy <folder> <name>…` put archives in and
   out of `auto_deploy`. `new` and `run` stay.
5. **Each deployment is a generation.** The workers extract an archive into the runtime cache under
   `<name>_<n>`, so `ctfroot` ends as MPS's does and a metric's archive label matches
   (`RuntimeCache.Open` takes the folder's name; the cache's own naming stands elsewhere).
6. **The helpers are builtins over device-style objects.** `prodserver.metrics.incrementCounter`
   and `setGauge` print the event line (`%g`, the archive `ctfroot`'s folder) after R2025b's checks
   in their order and words; `mps.cache.control`, `connect`, `configure`, `mps.sync.mutex`,
   `mps.cache.Connection`'s statics and the two enumerations' members are registered under their
   dotted names in every session; `MpsControllerObject`, `MpsCacheObject`, `MpsConnectionObject` and
   `MpsMutexObject` are `DeviceObject`s (a cache's keys read and written through two new hooks,
   `TryMember` and `TrySetMember`), `MatFileService` the MatlabTest provider. Redis and MongoDB are
   out of scope (decision KD8): their providers are known by name and refused.
7. **A mutex excludes.** R2025b's `TimedMATFileMutex` makes its lock under a unique temporary name,
   so a second `acquire` of the same mutex by another object always succeeds; JGraph's lease is
   `locks\<name>\expirationDate` under the service's folder, taken with an atomic create, so a
   second holder waits or fails until the lease expires (correctness outranks parity; recorded as
   a divergence, `mutex_second_acquire` and `mutex_second_own`).
8. **Found on the way.** `strrep` refused a char of one row or none made by `char` of an empty
   uint8 (a web request's empty body) as a multi-row input; the server listens on both loopbacks
   for `localhost` (a client resolving localhost tried `::1` first and its SYN hung); an option
   given twice in `main_config` takes the last value; dispatch sweep 29 for the seventeen new
   names (`dispatch_probe29.m`, every one a file under every pattern, the enumeration members
   unresolvable), their output counts audited (`exist` answers 0 for a package function `which`
   finds as an M-file; the audit's column is set to the file kind for them).

## Measured

- `run-k15.ps1` and `compare-k15.py` against muserve's recording: 519 of 575 cases agree, 56
  recorded divergences, 0 unexplained; a web handler's echo compared with its request headers sorted
  and the length the port's digits set replaced.
- `ProductionServerTests` (2, needing CPython: stage K14's, and the archive built in process from
  `src/k14` and `src/k15`, served in muserve's two sessions, the client's modes case for case with
  the recording, and the asynchronous protocol's states and codes), `ProductionServerPartsTests`
  (5: the metrics text from the recorded events, the config's sizes, the route table's precedence,
  the request registry's states, and the admin commands run as processes), and the fixture
  `k15_mps_cache` (230 lines from R2025b, 16 recorded divergences, both representations).

## Divergences

- **muserve has no asynchronous requests**; JGraph answers the documented protocol (every `async`
  case, and `path.query.async`).
- **`404 component not found` stops there**; muserve's phrase goes on with a C++ function's name.
- **A handler's `Content-Length` header is dropped**; muserve sends it beside the body's own.
- **The request headers reach a handler in Kestrel's order**, Host first; muserve keeps the wire's.
- **The metric functions' own frames are JGraph's builtins'**, not an M-file's lines (`metric.*`
  errors); the environment's `ctfroot` parent, `pwd` and `which` are the server's own (`env.*`).
- **The error stack of a MATLAB error has no `mpsExecuteInMatlab` frame** and JGraph's words stand
  for a dimension mismatch, too many outputs and an unassigned `varargout` (ADR 0270, seen again).
- **A metric event's archive outside a server is JGraph's `ctfroot` folder** (`net8.0`, where R2025b
  prints `R2025b`).
- **A mutex excludes** (decision 7).
- **A struct as a dynamic field name** (`c.(key)` with a composite key) is refused by the
  interpreter (`MATLAB:mustBeFieldName`); `get(c, key)` reads it.
- **`enumeration('mps.cache.Location')` answers nothing**: JGraph has no enumeration classes.
- **The echo of a logical** (`ans = true` for `ans = logical 1`, open item 194's family).
- **The cache's files are version 7 MAT-files with a cell of names**; R2025b writes version 7.3
  with a string array (JGraph's 7.3 writer refuses strings); both are read.
- **A composite key's fingerprint is SHA-256 of its large-notation JSON**; R2025b's is its own
  builtin's.

## Still open

- Item 211 is replaced by item 213: a worker's restart, memory limit and `--request-timeout` unmeasured
  (muserve refuses the interval option), `/api/statistics` and `/api/archives` disabled as muserve
  has them, protobuf (K16), the ASP.NET Core shared framework in the installers (K18), the HTTPS and
  access-control options, `mps-status`'s statistics and JSON forms, Redis and MongoDB providers, the
  enumerations as classes.
- Item 212 (`mps.json`'s leftovers), items 194, 170, 199 and 201, seen again.
