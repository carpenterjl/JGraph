# ADR 0357 — A version 7.3 model's component tree is saved, and `zip` and `unzip` encrypt

## Status

Accepted, 2026-11-14. Open items 147 and 172 (encryption only), chips wave 4, agent Z.

## Context

- **Item 147.** An `.mlapp` whose `appdesigner/appModel.mat` is a version 7.3 (HDF5) file keeps its
  MCOS subsystem as HDF5 objects. That covers one shipped app (`serdes/SignalIntegrityLink.mlapp`)
  and any app saved under MATLAB's version 7.3 MAT-file preference (`oi57_build`).
  `MatMcosFile` read only level-5 files. A save that changed `createComponents` spliced App
  Designer's copy of the code (ADR 0239) but left the tree, so App Designer showed the old
  components (ADR 0245's divergence). The status line said so.
- **Item 172.** ADR 0340 measured how `zip` and `unzip` split names from positionals, and gave
  R2025b's own refusals in its words. Encryption itself was still refused: `zip`'s `Password` with
  `EncryptionMethod` (ZipCrypto, AES-128, AES-256), and `unzip`'s `Password` on an encrypted entry
  (ADR 0258's divergence). .NET's ZIP classes neither encrypt nor decrypt.

## Decision

### A version 7.3 model is read as the level-5 file it stands for, and written back as version 7.3 (`MatV73Mcos`)

Both models were measured with `h5info` (`probe_h5`, `probe_kinds`). The two forms hold the same
things in different wrappings:

- `#subsystem#/MCOS` is a dataset of object references marked `MATLAB_class` `FileWrapper__` and
  `MATLAB_object_decode` 3. Its referents are the items of level 5's `FileWrapper__` cell, in order:
  - the `uint8` object table, byte for byte the level-5 one;
  - an empty value, a `canonical empty` dataset (level 5's bare element);
  - the stored values;
  - the three per-class cells.
- An object that a variable, a field or a cell element holds is its reference words. In version 7.3
  these are a `uint32` dataset whose `MATLAB_class` is the object's class, marked
  `MATLAB_object_decode` 3; in level 5 they are an opaque element.
- Inside the subsystem's values, a reference is a plain `uint32` array in both forms.
- Everything else uses the plain-value layout of `MatV73Reader` and `MatV73Writer`:
  - char is `uint16`, logical `uint8`;
  - an empty value is the list of its dimensions;
  - a cell is references into `#refs#`;
  - a struct is a group, and a struct array a group of reference datasets with no class;
  - an empty struct lists no fields.

  `probe_kinds` lists 20 combinations of class, datatype and attributes in the shipped model (the
  sample has a subset), and every one is carried.

`MatMcosFile.Read` sees a version 7.3 file (its description and the HDF5 signature) and carries it
to level-5 bytes with `MatV73Mcos.ToLevel5`:
- each root variable becomes a matrix element;
- the `FileWrapper__` referents become the cell's items, assembled in the layout
  `MatMcosWriter.WriteFile` writes (`MatMcosWriter.Level5File`).

Then it reads those bytes as before. It remembers the file's 116-byte description
(`Version73Header`). `Write` then carries the level-5 result back with `MatV73Mcos.FromLevel5`, a
whole new file through `MatV73Writer.Builder`:
- each variable at the root, every cell item and struct-array element in `#refs#`;
- the subsystem cell as `#subsystem#/MCOS` again;
- no `H5PATH` attributes, which nothing reads;
- the description the file had.

The builder gained `Listed`, `Numeric`, `CharUnits`, `EmptyOf`, `ObjectWords`, `Wrapper` and
`StructOf` for values carried as raw bytes, and `Finish` takes the description. A sparse array, a
function handle or an old-style object is refused (`NotSupportedException`). The tree is then left,
and the save says why. Neither model holds one.

So `DesignTreeWriter.Update`, `ReadGroups` and the save's status line treat a version 7.3 model as
they treat a level-5 one. The user's MAT-file preference is kept: a model read as version 7.3 is
written as version 7.3. The code copy is still spliced first (ADR 0239). The rewritten file holds
the spliced `code`, and `WithRunConfigurations` splices into the rewritten file, because
`MatV73Writer`'s layout is the one `CanSplice` accepts.

### `zip` and `unzip` encrypt and decrypt as R2025b does (`ZipEncryption`)

R2025b was measured first (`probe_zip`, `probe_zip2`, 2026-11-14):
- **Which entries are encrypted.** Every file with content is deflated and encrypted (flag bit 0,
  with a data descriptor). An empty file and a folder are written plain.
- **ZipCrypto** is PKWARE's traditional encryption: the 12-byte header, then the deflate stream.
- **AES** is WinZip's. The entry has method 99 and the `0x9901` extra field: version, `AE`, the
  strength (1 for AES-128, 3 for AES-256) and the real method 8. The data is a salt (8 or 16
  bytes), a two-byte password verifier, the deflate stream in counter mode, and ten bytes of an
  HMAC-SHA1 over it. A file under 20 bytes is AE-2 with its CRC written as 0; 20 bytes and over is
  AE-1. The method names are case-blind, and `aes128` and `aes256` are taken.
- **The password's bytes are its UTF-8.** A ZipCrypto archive of `'p' char(233) 'w'` decrypts with
  `70 C3 A9 77` and not with the Latin-1 bytes.
- **A wrong password** is `MATLAB:io:archive:extractArchive:incorrectPassword` ("The provided
  "Password" value is incorrect."), for both methods. No password on an encrypted entry is the
  `unableToWrite` refusal ADR 0340 already gave.
- **Extraction runs in order.** Entries before the one refused are written, and the output folder
  is made (`mixed_zc`, `mixed_aes`, `mixed_encfirst`). A password on a plain archive is ignored.

`ZipEncryption` implements both in managed code over `System.Security.Cryptography` alone:
- ZipCrypto's three keys and its CRC-32 table;
- PBKDF2-HMAC-SHA1 over 1,000 rounds (`Rfc2898DeriveBytes.Pbkdf2`) for the AES key, the HMAC key
  and the verifier;
- AES-ECB over a 16-byte little-endian counter from 1 for the counter mode;
- `HMACSHA1` for the authentication code, held in fixed time.

`zip` with a password writes the archive itself (`WriteArchive`):
- local headers with the sizes known, so ZipCrypto's check byte is the CRC's high byte;
- UTF-8 names marked by bit 11;
- no ZIP64. An archive that would need it is refused with `MATLAB:zip:openError`, and an
  unencrypted `zip` still goes through `ZipArchive`.

`unzip` still lists every entry through `ZipArchive` and writes plain ones from it. An encrypted
entry is read from the archive's own central directory (`CentralDirectory`, `StoredData`,
`Decrypt`), matched by index:
- ZipCrypto's check byte is the CRC's or, with a data descriptor, the time's high byte;
- AES checks the verifier and the authentication code, and AE-1 also the CRC.

A failed check is `incorrectPassword`. An encryption this does not read (PKWARE's strong
encryption, an unknown AES strength or method) is `JGraph:unzip:encryptionNotSupported`.

## Consequences

- A save of a version 7.3 app that changes `createComponents` brings App Designer's tree to the code
  and keeps every untouched component as App Designer saved it, as for a level-5 app (ADR 0245).
  The file stays a version 7.3 file. It is written afresh, uncompressed and without the objects
  the old file no longer reached. After the test's edit, the shipped app's model went from
  2,366,773 bytes to 1,892,744, and `U7bApp73`'s from 326,592 to 259,056.
- `AppDesignerModelSaveTests` now holds the version 7.3 shipped app to the same rules as the 43
  level-5 ones:
  - `EveryShippedModelWritesBackAsTheSameObjects`;
  - `EveryShippedTreeKeepsItsComponentsUnderItsOwnCode`;
  - `AnEditedShippedAppKeepsEveryOtherComponentAsSaved`.

  `AVersion73ModelsTreeIsLeft_AndTheSaveSaysSo` became `AVersion73ModelsTreeIsBroughtUp_AndStaysVersion73`.
  `TheShippedAppsGroupsAreReadAsTheProbeCountedThem` now counts SignalIntegrityLink's groups.
- `unzip` of an archive whose encrypted entry it cannot open writes the entries before it, where it
  wrote nothing before. This is R2025b's order. The check that no entry lands outside the folder
  still runs before anything is written (ADR 0258).
- `oi171_gzip_tar` was re-recorded:
  - four ADR 0258 rows now agree;
  - 16 rows are new: the content `unzip` decrypted, round trips of the three methods, wrong passwords, no password, a password on
    a plain archive, R2025b's own AES-256 (AE-2) and AES-128 (AE-1) archives, and a mixed archive
    with and without the password.

## Measured

- **R2025b reading what JGraph wrote (item 147)**, headless, over files the local tests wrote with
  `JGRAPH_DS6_OUT`.
  - `ds6_verify` (`DS6_ONLY=SignalIntegrityLink`, `DS6_PART=edits`) opened the shipped app and its
    edit through App Designer's own reader (`DeserializerFactory ... getAppData`). The edit is the
    first button's `Text` changed, a label removed and a button added. The verdict was "as saved":
    37 untouched components load as the original's, `closeButton` changed, `NewProjectLabel`
    removed, a button added.
  - `oi147_verify` read the edited shipped app and the edited `U7bApp73` (`Go` renamed `Run`):
    - the header is "MATLAB 7.3 MAT-file", and the stored end of file is the file's length;
    - `whos -file` and `load` give the same variables and classes as the original, with no
      warning, and `components.UIFigure` is a `matlab.ui.Figure`: 41 graphics objects with 8
      children for the shipped app, 5 for the sample;
    - `h5info` lists `/#refs#`, `/#subsystem#`, `/code` and `/components`;
    - `getAppData` has the same 19 (2) callbacks and no warning, and its tree shows `Edited in
      JGraph`, `Added` and `Run`.
- **Both directions (item 172)** (`cross_write`, `cross_read`). Each engine wrote a ZipCrypto, an
  AES-128 and an AES-256 archive of a tree that holds an empty file, an empty folder, a 3,000-byte
  binary and a file under 20 bytes. The password held a non-ASCII character. The other engine
  extracted every file byte for byte and refused a wrong password with `incorrectPassword`.

## Divergences

- **A version 7.3 model JGraph rewrote is not byte for byte what App Designer writes.** Its datasets
  are contiguous and uncompressed and carry no `H5PATH`, and its `#refs#` names are JGraph's.
  R2025b loads and reads it (Measured). App Designer's next save writes its own form over it.
- **An encrypted archive JGraph writes differs from R2025b's in its bytes.** It has no data
  descriptor and no Unix extra fields, it is made by MS-DOS, and its salts and headers are random.
  R2025b extracts it, and JGraph extracts R2025b's.

## Still open

- `unzip`, `gunzip` and `untar` from a URL stay ADR 0258's and ADR 0340's divergences: they need
  the network.
- MATLAB's App Designer window opening a version 7.3 app whose tree JGraph rewrote. The readers
  are App Designer's own, but the window needs a display and a person.
