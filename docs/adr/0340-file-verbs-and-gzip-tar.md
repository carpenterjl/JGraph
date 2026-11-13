# ADR 0340 — `mkdir`, `copyfile` and `movefile` answer as R2025b's, and `gzip`, `gunzip`, `tar` and `untar` join `zip`

## Status

Accepted, 2026-11-12. Open items 171, 172 (measured only), 190 and 204, taken together in one wave
because they share a file and a way of failing.

## Context

**The file verbs (items 190 and 204).** JGraph's `mkdir`, `copyfile` and `movefile` answered only
their status. A failure was an error in every form, with .NET's message and no identifier.
- `mkdir` of a folder already there said nothing.
- K7 taught `copyfile` folders and wildcards from MATLAB's documentation, not from a probe.
- `movefile` took neither form.

R2025b's answers were measured headless (`probe_fs`, `probe_more`; 87 and 56 cases). For each verb,
`[status, msg, msgID] = verb(...)`:
- With no outputs, a failure is an error. With any outputs it is status `false` and the error's words,
  and nothing is raised. That includes `s = verb(...)` with one output.
- `mkdir` of a folder already there succeeds. With fewer than two outputs it warns
  `MATLAB:MKDIR:DirectoryExists` ("Directory already exists."). With two or three it answers that
  message and identifier and warns nothing.
- `mkdir('')` is the same shape: `MATLAB:MKDIR:EmptyDirectoryName`, two lines of text.
- `mkdir` with a file in the way is `MATLAB:MKDIR:ExistsAsFile`: "Unable to create directory. <name>
  already exists as a file.", the name as given, or `fullfile(parent, name)`.
- What Windows refuses is `MATLAB:MKDIR:OSError` with the system's own message, line end included:
  - a file further up the path is code 3, "The system cannot find the path specified.";
  - `a<b` is code 123.
- A missing parent is made, in both the one- and the two-argument forms.
- Non-text arguments are `MATLAB:string` ("Argument must be a text scalar."), raised even with
  outputs. So is a fourth output (`MATLAB:maxlhs`).
- `copyfile` and `movefile` share their refusals, each under its own `COPYFILE` or `MOVEFILE` id:

| Case | Identifier | Message |
|---|---|---|
| Source missing, or a wildcard matching nothing | `FileNotFound` | `No matching files named '<full source>' were found.` |
| Target is the source | `SourceAndDestinationSame` | `Cannot copy or move a file or directory onto itself.` |
| A file's target folder is missing, or a wildcard's destination is a file | `resourceNotFound` | `The location '<full target>' was not found.` |
| Target is read-only, without `'f'` | `ReadOnly` | `Cannot write to destination: <full target>.  Use the 'f' option to override.` |
| A folder onto a file | `OSError` | `Destination <full> is not a folder.` then, on a new line, `When moving a source folder, the destination must also be a folder.` |
| A file to a new name ending in a separator | `OSError` | code 123's text, without a line end |
| An empty source or destination | `IllegalName` | `All arguments must be non-empty character vectors or string scalars.` |
| A mode other than `f` | `InvalidOption`, raised with or without outputs | `'x' is an invalid option.` |

- The destination is the current folder when left out.
- Every argument from the third is a mode, and `'f'`/`'F'` is the only one.
- A folder source:
  - `copyfile` merges its contents into the destination, which is made, nested parents included;
  - `movefile` renames it to a destination that is not there, and otherwise puts it inside under its
    own name, merging with a folder of that name.
- A wildcard in the last part: every match, files and folders, goes into the destination folder,
  which is made even for one match.
- Neither verb takes its own destination, or a folder holding it, along:
  - `copyfile('ci', 'ci/sub/inside')` copies `a.txt` and skips `sub`;
  - `copyfile('cj/*', 'cj/sub')` copies `a.txt` into `sub` and skips `sub`.

**The archivers (item 171).** R2025b's `toolbox/matlab/io/archive` holds `gzip.m`, `gunzip.m`,
`tar.m` and `untar.m` beside the `zip.m` and `unzip.m` stage K3 transcribed. They are older code than
`zip`'s, without an arguments block (`narginchk`, `checkFilesDirInputs`, `parseArchiveInputs`,
`parseUnArchiveInputs`). The four write through compiled helpers (`compressgz`, `uncompressgz`,
`createArchive`, `extractArchive`), so those answers were measured (`probe_arc`, 117 cases). Every
message is in `resources/MATLAB/en/io/archive/*.xml`.

**Encryption and URLs (item 172).** ADR 0258 refuses `zip`'s `Password`/`EncryptionMethod`,
`unzip`'s `Password` on an encrypted entry and `unzip`'s URLs in JGraph's words. It never measured
how R2025b splits a positional argument from a name, or R2025b's own refusals.

## Decision

### `mkdir`, `copyfile` and `movefile`

The three are one shape, `RegisterFileVerbs`:
- a body answers an identifier and message for a success (empty for most), or throws
  `FileVerbRefusal`;
- with no outputs a refusal is raised; with any outputs it is status false and its words;
- a success's message warns with fewer than two outputs and is answered with two or three.

The builtins know when they are discarded (`KnowsWhenDiscarded`), so `mkdir x` and `copyfile(a, b);`
as statements ask for nothing, as in R2025b, and still bind no `ans`.

Each verb follows the table above:
- A source is found in the current folder first and then through the read resolver (a script's folder,
  a deployed program's frozen path). A pattern or a missing name is quoted at its full path in the
  current folder.
- Wildcards match with `MatchType.Simple`, so `*.txt` does not match `x.txt1` through an 8.3 name.
- Messages carry full paths, as R2025b's do.
- `mkdir`'s system text is the Win32 message with `\r\n` added, as R2025b reports it.
  `copyfile`/`movefile`'s code 123 text has no line end, as measured.

`fileattrib(name, mode)` now sets `w`, `h`, `s` and `a` with `+`/`-` (`x` and the users argument
change nothing). The fixture needs it to make a read-only file for `'f'`.

### `gzip`, `gunzip`, `tar`, `untar`

The four are transcribed into `JgsBuiltins.Archive.cs`:
- They reuse zip's entry walk. `ArchiveEntries` now takes the function's name for its identifiers
  and messages, and `includeEmptyDirs`: `gzip` and `gunzip` pass false, so an empty folder adds
  nothing; `tar` passes true, so it is an entry.
- The empty name is a file that does not exist, as `dir('')` lists nothing.
- `checkDuplicateEntries`, `filterOutArchiveFile` and `checkEmptyEntries` are shared with `zip`:
  `CheckArchiveDuplicates`, `FilterOutArchiveSelf`, `ArchiveNoEntries`.
- `gzip(files, outputfolder)` compresses each entry to its name plus `.gz`:
  - beside the source (`fullfile(fileparts(entry), name)`), or in the output folder, flattening a
    folder's tree;
  - an output folder is made when missing, and its topmost new folder is removed again when
    anything fails (`dirCreated`);
  - `invalidDirname` formats a number as `{0,number,integer}`.
- `gunzip(files, outputfolder)`:
  - each entry is written to its name less its last extension (`a.txt.gz` to `a.txt`,
    `renamed.dat` to `renamed`);
  - a file not starting 1F 8B is `notGzipFormat`;
  - `checkFilesURLInput` checks that a single name exists, or exists with `.gz` added, and the
    entries are still taken from the name as given (so `gunzip('x/c.m')` with only `c.m.gz` there is
    `fileDoesNotExist`);
  - R2025b's same-location refusal is transcribed: `destinationSameAsSource` when no folder was made
    and an entry without an extension would land on itself;
  - a target that is its own source is `OverwriteOpenFile`.
- `tar(tarfile, files, rootfolder)`:
  - zip's entries, written by `System.Formats.Tar` as POSIX (pax) tar;
  - a name ending `.tgz` or `.gz` (any case) is gzip-compressed and kept, and any other gets `.tar`
    unless it ends `.tar` in any case;
  - the refusals are zip's under `MATLAB:tar:*`, with `invalidFileString` for a name that is not text.
- `untar(tarfile, outputfolder)`:
  - the archive is found as given, then with `tgz`, `tar.gz`, `tar` and their upper-case forms
    added, in that order;
  - it is uncompressed when it starts with gzip's magic, whatever its name;
  - it must start with a tar header whose checksum holds, else `invalidTarFile` at its full path;
  - every entry is checked before anything is written, and names are answered as unzip's are.

### Names and values in `zip` and `unzip` (item 172, measured)

`ArchiveOptions` follows the arguments block as measured:
- A name may be any leading part of `Password` or `EncryptionMethod`, in any case (`'Pass'` is
  `Password`).
- It starts the pairs only where an even count follows. So `zip(f, files, 'Password')` makes
  `Password` the root folder (`nonExistDir`), and `unzip(f, 'Password', 'x')` gives no output folder.
- Past the last positional, an odd remainder that starts with a name is "Invalid argument list. Make
  sure name-value arguments include both the name and the value."; one that does not is the
  positional refusal, as before.
- A pair whose name is neither is "Invalid argument name 'Bogus'. Name must be 'Password' or
  'EncryptionMethod'." (`unzip`: "Use 'Password'.").

Every refusal R2025b gives itself is now given in its words:
- a value that is not text, or is empty: "Invalid value for 'Password' argument. ...";
- an `EncryptionMethod` outside the set: `mustBeMember`, listing all five on Windows and macOS;
- one name without the other: `MATLAB:zip:missingEncryptionMethod` / `missingPassword`;
- `unzip` of an encrypted entry with no password:
  `MATLAB:io:archive:extractArchive:unableToWrite`, its two lines.

JGraph's refusal stays where R2025b would encrypt or decrypt, and where it would download.
Encryption and URLs are not implemented: .NET's ZIP classes neither encrypt nor decrypt.

## Consequences

- Two parity fixtures were recorded from R2025b 25.2:
  - `oi190_204_file_verbs`: 104 lines, none divergent;
  - `oi171_gzip_tar`: 161 lines, seven of them recorded divergences.
  - `k3_zip_unzip` is unchanged and still green.
- Every name on the four's dispatch sweep (42e) goes to a shadowing file under all 37 patterns.
  `which` calls each an `mfile`, and `nargout` is -1. The coverage count moves to 1,280 of 2,024.
- A script that reads `ok = mkdir(...)` or `[ok, msg] = copyfile(...)` now gets `false` and the words
  instead of an error. A statement call still raises, now with R2025b's identifier.

## Divergences

- **`untar` refuses an entry that would land outside its folder**, as `unzip` does (ADR 0258). R2025b
  writes `../../twin/a.txt` above the output folder. JGraph writes nothing and refuses the archive
  with `JGraph:untar:entryOutsideFolder` (`oi171_gzip_tar`'s `untar_dotdot`).
- **`gunzip` and `untar` do not download a URL** (`JGraph:gunzip:urlNotSupported`,
  `JGraph:untar:urlNotSupported`). R2025b fetches http, https, ftp and file URLs first. A file URL is
  the fixture's row, `untar_file_url`.
- **`zip` with both `Password` and `EncryptionMethod`, and `unzip` of an encrypted entry with a
  password, are refused** as ADR 0258 records. This includes a wrong password, which R2025b answers
  `incorrectPassword` (fixture rows `zip_encrypt`, `zip_encrypt_lower_names`,
  `unzip_encrypted_password`, `unzip_password_as_name`, `unzip_file_url`, all ADR 0258's).
- **`movefile` of a folder into its own subtree is refused.** R2025b answers status 1 having moved part
  of it: `movefile('ms', 'ms/sub/inside')` moved `a.txt` into `inside` and left `ms` and `sub`. JGraph
  raises `MATLAB:MOVEFILE:OSError` with the system's message. Not in a fixture.
- **`gzip`, `gunzip` and `tar` write failures, and `untar`'s, are JGraph's words**
  (`JGraph:gzip:writeError`, `JGraph:gunzip:writeError`, `MATLAB:tar:openError`, `MATLAB:untar:openError`).
  R2025b's compiled helpers were not driven to fail.

## Still open

- `dir(fullfile('cs', '**'))` with a relative folder lists nothing in JGraph. An absolute folder
  works, and the fixture uses one. This is outside this wave's files.
- `fileattrib`'s answer is still JGraph's struct (no `[status, values, msgID]`), and its set form
  is the minimum the fixture needs.
- `gunzip`'s and `untar`'s URL downloads, and encryption in `zip` and `unzip`, stay unimplemented.
