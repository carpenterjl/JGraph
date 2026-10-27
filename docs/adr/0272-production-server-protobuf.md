# ADR 0272 — The Production Server's protobuf wire format for MathWorks' own client libraries

## Status

Accepted, 2026-10-26. Compiler and SDK plan, stage K16 (decisions KD8 and KO6).

## Context

MATLAB Production Server's client libraries for Java, .NET, C and Python do not speak the JSON of
stages K14 and K15: they `POST` `application/x-google-protobuf` bodies, Protocol Buffers messages
of a schema MathWorks does not publish as `.proto` text but ships generated code for. Stage K16 makes
`jgraph-mps` answer those libraries exactly as R2025b's `muserve` (the one-archive Production Server
of ADR 0271) answers them, so a client program written against MathWorks' libraries runs unchanged
against JGraph's server.

**The schema.** Recovered from the Python client's generated `_pb2` modules (the plan's
`mps_proto.txt`): `MATLAB_Params {nargout, rhs}`, `MATLAB_Result {lhs, error}`,
`MATLAB_Error {message, identifier, stack {file, name, line}, report}` and `MATLAB_Array`, whose
`type` is an `MWType` (the ten numeric classes, LOGICAL, CHAR, STRUCT, CELL, SPARSE, OBJECT,
HANDLE_OBJECT_REF, ARRAYREF, ENUMERATION, STRING), with `dimension`, `memoryLayout`, `isComplex`, one
payload message per type (`uint8` and `int8` as bytes, `int16` and `uint16` as varints, `int32` and
`uint32` fixed, `int64` and `uint64` varints, `single` and `mwdouble` fixed, each with
`imag_elements`; `logical`, `mwchar` code units, `cell`, `struct_` with `field_names` and
`elements`, `sparse` with `type`, `ir`, `jc` and a values message, `stringElements` with the code
units of every string in a row and `stringSizes`), a `sharedData` string table for the names an
enumeration, an object or a handle reference uses, `classNameIndex` and `objects` for a value
object, `objectRefs` for a handle, `enumerationClassName` and `enumerations` for an enumeration.
It is proto3: repeated scalars are packed, a field explicitly set is written even at its default.

**The oracle.** `record-k16.ps1` serves stage K15's archive (`k15comp.ctf`, R2025b's `mcc -W CTF`
build of `src/k14` and `src/k15`) with muserve and runs four of MathWorks' client libraries against it:

- **Python**, `toolbox/compiler_sdk/mps_clients/python/dist` under CPython 3.12 (its
  `matlabmultidimarrayforpython.pyd` is a stable-ABI module), through `src/k16client/k16client.py`:
  mode `proto` calls through `MWHttpClient` and its builders and decoders; mode `raw` builds
  `MATLAB_Params` by hand from the generated classes (every type, shape and fault the schema allows)
  and prints each reply as protobuf text and as the bytes muserve wrote, so JGraph's bytes are
  compared byte for byte; modes `hidden` and `size` run against a session with
  `--hide-matlab-error-stack` and a 1 KB request limit; mode `edge` holds the messages that end or
  hang muserve's worker, each run against a muserve session of its own.
- **.NET**, `MathWorks.MATLAB.ProductionServer.Client.dll` (a .NET Framework 4.5 assembly with
  Google.Protobuf embedded as a resource), through `src/k16dotnet/k16dotnet.cs`, compiled by the
  Framework's `csc.exe`: the component proxy (`Invoke<T>`, the typed and untyped output forms, the
  marshaling rules' struct classes and flags) and the REST classes (`MATLABParams`, `MATLABResult`),
  whose request and reply bytes are printed too.
- **C**, `mpsclient.dll` with its `mps/client.h`, through `src/k16c/k16c.c` compiled by MSVC
  (`src/k16c/build-k16c.ps1`): `mpsArray` values of every class the API makes, `feval` with each
  output count, and the error information it reports.
- **Java**, `mps_client.jar`: no Java compiler is installed here, so `src/k16java/probe_k16_java.m`
  drives its REST classes (`MATLABParams.newInstance`, `MATLABResult.newInstance`) from R2025b's own
  JVM under `matlab -batch`, with MATLAB's HTTP classes carrying the bytes (a `MessageBody` sends raw
  bytes through its `Payload`, not its `Data`).

`run-k16.ps1` does the same against JGraph's build of the archive served by `jgraph-mps` and
`compare-k16.py` compares case for case.

## Decision

- **No library.** `JgsProtobufWire` is the wire format (varints, fixed fields, length-delimited
  fields, packed repeated scalars, unknown fields skipped) and `JgsMpsProtobuf` the schema, written
  by hand in `JGraph.Scripting` beside `JgsMpsJson`. Google.Protobuf for .NET would need generated
  classes or a descriptor built by hand for a schema this small; the reader and the writer are two
  hundred lines each and the bytes are checked against muserve's.
- **The reader** takes an array as muserve takes a well-formed one: the dimensions as given (none is
  0-by-0, one is a column, a trailing 1 past the second is dropped), the type from `type` or, when
  it is absent, from the payload present, elements column-major (a `ROW_MAJOR` layout is transposed
  as muserve transposes it), complex when `imag_elements` are present whatever `isComplex` says,
  char code units kept to 16 bits, a struct's `elements` element by element with each element's
  fields in order, a missing string as the one code unit 0x1FFFF, an enumeration by the string
  table, elements past the dimensions' count ignored. **Where muserve's worker reads past what the
  message holds, ends, or hangs, JGraph refuses the request** with `400 InvalidInputArgs Invalid rhs.`
  (muserve's own refusal for garbage): fewer elements, imaginary parts, cells, field values or string
  sizes than the dimensions need, a payload absent for a non-empty array or of another type than
  `type`, two payloads, `isComplex` without parts, an `UNKNOWN` layout, OBJECT, HANDLE_OBJECT_REF and
  ARRAYREF types, a shared array id, a sparse `jc` or `ir` out of range, a field name MATLAB cannot
  hold or one twice, a message with a field number the schema lacks or JSON text as the body. Each
  is a recorded divergence; correctness outranks parity.
- **The writer** writes what muserve writes, byte for byte where JGraph holds the same value: `type`,
  `dimension` packed, the string table when one is needed, `memoryLayout` COLUMN_MAJOR and
  `isComplex` always written, then the payload; a struct array's fields element by element; a
  sparse array as compressed columns; an enumeration's class and member names in the table (an
  empty first entry, `startIndex` one past the count) and each element an `Object` naming its
  member; a datetime, a duration and a categorical as the value objects MATLAB serializes them as
  (`TimeStamp`, `Year` … `Second`, `__metaData` with `TimeZone` and `Format`, `__externalDimensions`;
  `millis` and `fmt`; `categoryNames`, `codes`, `isProtected`, `isOrdinal`), each an OBJECT of one
  element whose properties carry the array's shape; a function handle, a containers.Map and any
  handle object as a HANDLE_OBJECT_REF with a session id and an object number of JGraph's own; a
  table, a dictionary and other value objects as an OBJECT with their class name and, for a user
  class, its properties. No outputs is an empty body with `Content-Length: 0` and no `Content-Type`,
  as muserve answers `nargout` 0 or none.
- **Errors** are `MATLAB_Result.error` with `message`, `identifier` and the M-code frames, each
  frame's `file`, `name` and `line`; a local function is named by its own name, as R2025b's server
  names it in JSON and protobuf alike (the JSON path now does the same); with
  `--hide-matlab-error-stack` no frames. A negative `nargout` is muserve's own error
  `vector too long` with no identifier; an enumeration member the class lacks `Invalid enumeration`;
  an unknown class `Class <name> not found.`.
- **The server** (`ServerHost`, `WorkerPool`, `WorkerMain`, `MpsCalls.CallProtobufAsync`) routes
  by `Content-Type` as before: `application/x-google-protobuf` in any case, with any charset, goes
  to the worker as base64 in the worker frame and comes back the same way; the reply is written with
  `application/x-google-protobuf` and its length. The asynchronous protocol (ADR 0271) takes a
  protobuf body too and `GET …/result` answers in the request's format.

## Measured

`run-k16.ps1` against JGraph's build of the archive served by `jgraph-mps`, compared by
`compare-k16.py` with the recording (`recordings/k16_server.out.txt`): 698 of 743 cases agree, 45 are
the divergences named below, none is unexplained (the Python client's modes 146 proto, 210 raw, 5
hidden and 2 size cases; the .NET client's 194, the C client's 136, the Java client's 41; the 9
edge cases). The .NET client's bytes and values
and the C client's values agree with muserve's but for the 64-bit and sparse classes JGraph holds
differently; the Java client, driven from R2025b's JVM, decodes JGraph's replies as it decodes
muserve's, its own faults included (a cell passed as separate inputs and `[]` as none are MATLAB's
conversions, an error reply a NullPointerException inside the client).

What the recording measured of muserve, beyond the schema (`k16_server`):

- **Requests.** `nargout` absent is 0 and a negative one is the error `vector too long` with no
  identifier (nargout.*); an array's `type` absent is the payload's (in.type.absent); no
  `dimension` is 0-by-0, one is a column, `[2 2 1]` is 2-by-2 (in.dims.*); elements past the count
  are passed over (in.count.long); `ROW_MAJOR` is transposed (in.layout.row.major); imaginary parts
  make an array complex whatever `isComplex` says, and a zero imaginary part keeps it complex
  (in.complex.*); a code point past U+FFFF or a code unit past it is kept to 16 bits
  (in.char.supplementary, in.char.past.utf16); a char array of three dimensions is taken; a struct's
  `elements` go element by element (in.struct.array.twofields), a duplicate field name is a 400 and
  a name MATLAB cannot hold is taken and fails on use; a string's missing form is the one unit
  0x1FFFF (out.stringmissing) and `stringSizes` of 0 is an empty string (in.string.missing); a sparse
  array's `ir` out of range is dropped (in.sparse.badir); an enumeration's class and members come
  from the string table, an unknown member is the error `Invalid enumeration` and an unknown class
  `Class <name> not found.` (in.enum.*); `arrayID`, `dataFrame` and ARRAYREF are a 400 (in.ids.set,
  in.type.arrayref); a field number the schema lacks is passed over (body.extra.field); JSON text
  as the body is a 400, a protobuf body under the JSON content type `400 InvalidJson` and under none
  `400 MissingContentType`, the protobuf type with a charset or in another case is read as protobuf
  and an `Accept` header is passed over (body.*, type.*, accept.*, as stage K14's rules); four 0xFF
  bytes and a cut message are `400 InvalidInputArgs Invalid rhs.`; an empty body is a call with no
  arguments.
- **Where muserve reads past its message** it answers garbage: fewer elements than the dimensions
  (in.count.short), fewer imaginary parts (in.complex.imag.short), a payload absent (in.payload.absent)
  or of another type (in.type.mismatch, in.type.mismatch.char), two payloads (in.payload.two).
- **Where its worker ends** (the server answers `500 InternalServerError The pipe has been ended` and
  restarts it): `isComplex` without parts, a struct with fewer field values than its elements need,
  a `jc` out of range, an `UNKNOWN` layout, the OBJECT and HANDLE_OBJECT_REF types, string sizes
  fewer than the dimensions (`500 an unknown error occurred`); and **where it hangs**: two zero bytes
  after a valid message (body.trailing) are answered as if they were not there, and the worker then
  never answers the next request, whatever it is, so every later case of the session times out (the
  probe's first runs blamed the case that followed; each ran clean in a session of its own). The
  edge cases each run against a muserve session of their own (edge.*).
- **Replies.** An output's array carries `memoryLayout: COLUMN_MAJOR` and `isComplex: false`
  explicitly; `nargout` 0 is an empty body with `Content-Length: 0` and no `Content-Type`; errors are
  `message`, `identifier` and the M-code frames, none with `--hide-matlab-error-stack`
  (hidden.*); a function handle and a containers.Map are HANDLE_OBJECT_REF with a session id and an
  object number; a datetime, a duration, a categorical, a table and a dictionary are OBJECT with
  MATLAB's serialized properties; a 1-by-2 categorical is an OBJECT of 1-by-1 whose `codes` carry
  the shape; a request past `--request-size-limit` is the 413 of ADR 0271 (size.large).

## Divergences

- **Where muserve reads past its message, ends or hangs, JGraph refuses the request** with
  `400 InvalidInputArgs Invalid rhs.`: in.count.short, in.complex.imag.short, in.payload.absent,
  in.payload.two, in.type.mismatch, in.type.mismatch.char, in.sparse.badir, in.struct.badname (muserve
  makes the struct and fails on its use), and the edge cases that end muserve's worker (correctness
  outranks parity); two zero bytes after a message are nothing to JGraph's reader, as to muserve's,
  and the request after them is answered (edge.body.trailing.then) where muserve's worker hangs.
- **A complex number whose imaginary part is zero is real in JGraph** (in.complex.zero.imag).
- **A char array of three dimensions is refused** (in.char.nd; ADR 0270, seen again).
- **int64 and uint64 past 2^53 are doubles** (ADR 0174, seen again: in.int64.row, in.uint64.row,
  out.int64, the .NET and C clients' 64-bit cases).
- **A sparse logical, single or complex array is double** (ADR 0266, seen again: out.sparselogical,
  in.sparse.logical, in.sparse.single, the C client's sparse cases).
- **A handle reference names JGraph's own session and object** (out.handle, out.map); muserve's
  names its worker's, and no client reads either back.
- **A table and a dictionary are bare objects**: the class name and no properties, where muserve
  serializes their internals (out.table, out.dict); a datetime, a duration and a categorical are
  written as muserve writes them.
- **An error's frames name each file where the server extracted it**, so an error reply's length
  differs (the comparison replaces it); a local function is named by its own name in both formats,
  as R2025b names it, where JGraph's own stack says `file>local`.
- **JGraph prints the digits of a double or single its own way** in k14describe's `%.17g`
  (in.double.precise, in.single.row; ADR 0270, seen again).
- **Each output carries its own string table** (echo.twoenums); muserve writes the first output's
  table only, and the second enumeration's member index points into a table it never wrote (a
  client decoding both reads the wrong member), so JGraph's bytes are the readable ones.
- **muserve's worker is replaced where it ends, and its call count restarts**: in.sparse.badir ended
  it in the recording where an earlier run read garbage, so the .NET and C clients' `state.*` counts
  start again where JGraph's worker, which refused the request, counts on.
- **`?mode=async` starts an asynchronous request** (query.async); muserve has none (ADR 0271).

## Still open

- Item 214: the object forms of MATLAB's other value classes, shared arrays and data frames, the
  Java client in the test suite (no JDK here), the asynchronous protocol's protobuf result.
- Item 213 amended: protobuf no longer among the server's leftovers.

## Consequences

- A program written against MathWorks' MPS client libraries for Python, .NET, C or Java calls
  `jgraph-mps` as it calls MATLAB Production Server, with the values it sends and receives the same
  but for the classes JGraph holds differently (64-bit integers past 2^53, sparse logical and single).
- The server refuses what would make muserve's worker read garbage, end or hang; a client that sends
  such a message gets a 400 at once instead of a 500 or a timeout.
- `JgsProtobufWire` is a general proto3 wire reader and writer; a later stage that needs another
  schema (K18's installers do not) can build on it without a library.
