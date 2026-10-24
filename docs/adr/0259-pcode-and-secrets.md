# ADR 0259 — P-code, `mcc -j` and the secret functions, without encryption

## Status

Accepted, 2026-10-24. Compiler and SDK plan, stage K3b.

## Context

Stage K3 wrote the archive with its code stored as written. The plan's K3b was to add R2025b's
protection levels: its always-on AES encryption, `-s` (an obfuscated archive), `-j` (P-code),
`-k` (an external key and its loader) and `-J` (secrets embedded in the archive), each a value of
the manifest's `protection` attribute.

Two decisions shaped the stage:

- **KO1 (2026-10-23):** the default is open, a readable archive, where R2025b always encrypts.
- **No encryption (2026-10-24).** The user directed K3b to go ahead without any encryption. So
  the AES level, `-s`, `-k` and `-J` are not built. What is built is the part that needs none:
  `pcode` and P-files, `mcc -j` as an archive of P-files, and the secret functions with the
  environment-variable override a compiled program honours.

What R2025b does was measured first, by `record-k3b.ps1` (`tools/matlab-checklist/compiler-probes`):

- **`probe_k3b_pcode`** (`k3b_pcode.out.txt`). `pcode` is a `.m` file in R2025b
  (`toolbox/matlab/general/pcode.m`), so its argument handling was transcribed.
  - A P-file starts `v01.00v00.00`. The same source gives the same bytes twice, and `-R2007b`
    writes the same file as the default.
  - A P-file is found before the `.m` beside it. `exist` answers 6, `which` names the `.p`, and
    `type` of the bare name prints `'f.p' is a P-file.`
  - Its error frames report line 0, and a local function's frame carries the file's name.
  - A P-file older than its source still runs, with `MATLAB:pfileOlderThanMfile`, once per read.
  - A changed byte is `MATLAB:corruptPfile_3`; a short file or one that is not a P-file is
    `MATLAB:shortPfileFormat` with its path. `exist` still answers 6 for it.
  - Output goes to the current folder, under the source's `private`, `+package` and `@class`
    folders, or beside the source with `-inplace`.
- **`probe_k3b_secrets`** (`k3b_secrets.out.txt`), under `matlab -batch`, where the vault is
  unavailable.
  - `getSecret`, `isSecret` and `getSecretMetadata` refuse as `VaultNotFound`. `setSecret`,
    `removeSecret`, `setSecretMetadata` and `listSecrets` refuse as `VaultNotSupported`.
  - Each checks its arguments first, with the validators' messages, and `setSecretMetadata`
    checks its dictionary (configured, text keys, cell values).
- **`probe_k3b_build`** (`k3b_build.out.txt`) built the same program plain and with `-s`, `-j`,
  `-s -j`, `-k` and `-J`, and `k3b_run.out.txt` ran each.
  - Every build encrypts each code file (`V2MCC8000MEC`), even in the extracted copy.
  - `-j` stores each code file as a `.p`, the main included, and leaves a file of comments
    (`Contents.m`) as it is. `which` names the `.p`, `exist` answers 6, and an error reports line 0.
  - `-J` cannot be built under `-batch`: the vault is unavailable, so the build fails with
    "Failed to embed secrets into CTF".
- **`probe_k3b_secrets_build`** (`k3b_secrets_run.out.txt`) ran every secret function in a
  compiled program, with and without the variable set.
  - `getSecret` answers the environment variable of the same name as a string, the name's case
    not mattering. A name the environment lacks is `SecretValueNotFound`.
  - `isSecret` answers whether the variable is set.
  - With no output, `getSecret` prints a row of asterisks as long as the value, between blank lines.
  - `getSecretMetadata` refuses a missing name as `SecretValueNotFound`, and fails for a set one
    with `MATLAB:authnz:secretapis:InternalError`.
  - `listSecrets`, `setSecret`, `removeSecret` and `setSecretMetadata` are not in a compiled
    program at all.

## Decision

### The P-file (`JgsPcode`)

- **The format is JGraph's own.** It holds the file's tokens as the lexer read them, with the
  comments gone and every position 0.
  - The 16-byte header `jgraph-pcode-01\n` comes first.
  - Then a string table, the tokens (each type stored by name, a flags byte, the text, and a
    number's value), and last the SHA-256 of everything before it.
  - The bytes depend on the source alone.
- **It is not a protection.** Nothing is encrypted, and anyone with the format can read the
  names and literals back. It is a precompiled form that drops comments and line numbers.
- **Running one.** The parser takes the stored tokens (`Parser.Parse` over a token list), so a
  P-file runs as its source did and every frame in it reports line 0. `ME.stack` names a P-file's
  frame after the file, whichever of its functions was running.
- **Refusals.** A short file, or one without the header, is `MATLAB:shortPfileFormat` with its
  path. A hash that does not match, or tokens that do not read or parse, is
  `MATLAB:corruptPfile_3`. A P-file MATLAB's `pcode` wrote is `JGraph:pcode:MatlabPfile`, by name.

### P-files on the path

- **Precedence.** A folder's `.mlapp` comes first, then its `.p`, then its `.m`. The `.p` before
  the `.m` is measured. The `.mlapp` before the `.p` is MATLAB's documented order and not measured.
  This holds in `private/`, for `run` with no extension, and in a deployed program's frozen path.
- **The file index** lists each folder's P-files too, and a lookup probes the disk for a `.p`
  only where the index has seen one, so a folder without P-files costs no extra probe.
- **A newer `.m` beside a P-file** is warned about (`MATLAB:pfileOlderThanMfile`, R2025b's text)
  each time the P-file is read; the P-file still runs.
- `exist` answers 6, `which` names the `.p`, and `type name` prints `'name.p' is a P-file.`. The
  dependency analysis walks a P-file's tokens.

### `pcode`

`pcode` is transcribed from R2025b's `pcode.m`:

- its flags, read from the last argument back, and their refusals (`UnknownFlag`,
  `IncompatibleFlags`, `R2022aRequired`, `ArgMustBeString`);
- the way an argument becomes files: a folder as `*.?`, a name with no extension or `.p` as its
  `.m`, a wildcard, and `which` for a bare name;
- its warnings, which skip an argument (`BadPath`, `Ambiguous`, `NotPath`, `NotFileName`,
  `FileUnsupported`, `FileNotFound`);
- where the P-file goes (see Context), and a `rehash` at the end.

The file is parsed before anything is written, so a syntax error stops `pcode` there, as R2025b's
check does. `-R2022a`, `-R2007b` and `-nodebug` are checked and otherwise change nothing, since
there is one format. `-nocheck` is accepted, and the file is still parsed.

### `mcc -j` (`JgsPcode.PcodeBuild`)

- It turns a `CtfBuild` into one whose `.m` files are P-files beside where they were
  (`CtfBuild.MadeFiles`). The main and each public function's `file` point at the `.p`.
- A file with no statements stays an `.m`.
- The manifest's `protection` is `pcode` (`CtfProtection.PCode`).
- A deployed program whose main is a P-file runs it from its tokens.

`JGraph.Deploy` cannot reference the engine, so the P-coding lives on the scripting side, and the
writer only takes the made files' bytes.

### The secret functions

They are `getSecret`, `isSecret`, `getSecretMetadata`, `setSecret`, `removeSecret`,
`setSecretMetadata` and `listSecrets`, with R2025b's arguments blocks and messages.

- **An ordinary session** answers as R2025b's `-batch` does, since JGraph has no vault: the
  refusals above, after the arguments are checked.
- **A deployed session** reads the environment.
  - `getSecret` answers the variable's value as a string, or prints the asterisks with no output.
  - `isSecret` answers whether the variable is set.
  - `getSecretMetadata` answers an empty dictionary for a set name.
  - The four functions R2025b's runtime lacks are withdrawn, so a call is an undefined function.

## Consequences

- K6's `mcc -j` and `compiler.build`'s equivalent call `JgsPcode.PcodeBuild` before writing.
  R2025b's build also writes `<name>_secrets_manifest.json` and warns when the code calls
  `getSecret` (`k3b_build.out.txt`); K6 owns that warning.
- A runtime that meets `protection="pcode"` reads it; one older than this stage refuses it as newer.
- `secretID` is not built (open item 173).

## Measured

- **The fixture** `k3b_pcode_secrets` prints R2025b's 134 lines here, 3 of them recorded
  divergences, in both representations. It covers:
  - `pcode`'s output places, flags, refusals and warnings;
  - a P-file's `exist`, `which`, calls, `mfilename`, `nargin` and `nargout`, error frames,
    `func2str`, `functions`, `feval`, `type`, `run`, and a class's P-file;
  - precedence and the stale warning;
  - damaged files;
  - every secret function's refusals.
- **`JgsPcodeTests`** (6) cover the header and determinism, a P-file parsing as its source with
  every position 0, the syntax error, damaged files and MATLAB's own P-file, and `PcodeBuild`
  through the writer and reader.
- **`DeployedSessionTests`** gains two cases:
  - an `mcc -j` archive run end to end (`which`, `exist`, line 0, `Contents.m` kept);
  - the secrets in a deployed program against `k3b_secrets_run`.
- **Dispatch sweep 21** found all eight names going to a shadowing file under every pattern, as
  `.m` files do. `pcode`, `setSecret`, `removeSecret` and `setSecretMetadata` have no outputs;
  the others have one.
- **Speed.** 100,000 calls of a one-line file function took 15.5–15.7 s here and 15.4–15.7 s
  at HEAD, run back to back in a Debug build.

## Divergences

- **A P-file is JGraph's own format.** Its header is `jgraph-pcode-01` and its length is not
  R2025b's. A P-file MATLAB wrote is refused by name, and R2025b will not run JGraph's.
- **A P-file is not encrypted.** R2025b's are, and its `mcc -j` encrypts them again.
- **A syntax error in `pcode` is JGraph's parser's message.** It has no identifier where R2025b
  has one (`MATLAB:m_improper_grouping`), and `-nocheck` gives the same error where R2025b's is
  `MATLAB:screen`.
- **`-R2022a`, `-R2007b` and `-nodebug` change nothing.**
- **Code in an archive is never encrypted.** The AES default, `-s`, `-k` and `-J` are not built
  (decision of 2026-10-24).
- **JGraph has no vault.** An ordinary session refuses as R2025b's `-batch` does, in the desktop too.
- **`getSecretMetadata` of a secret from the environment answers an empty dictionary.** R2025b's
  runtime fails with `MATLAB:authnz:secretapis:InternalError`.

## Still open

- Item 173: `secretID`.
- Item 174: help text from the `.m` beside a P-file (`help` reads no file's help in JGraph yet).
- Item 175: about 0.15 ms per call of a file's function in a Debug build, seen while measuring
  the probe's cost.
- Encryption, `-s`, `-k` and `-J` stay unbuilt unless the decision of 2026-10-24 changes.
