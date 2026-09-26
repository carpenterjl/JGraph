# jgtestlib.dll: provenance

Built from `tests/Interop/native/jgtestlib.c` by `tools/interop/build-testlib.ps1`. Do not edit
this file by hand; re-run the script, which rewrites it.

| | |
| --- | --- |
| Built | 2026-09-26 |
| Visual Studio | Visual Studio Build Tools 2022 17.14.37710.0 |
| Compiler | Microsoft (R) C/C++ Optimizing Compiler Version 19.44.35229 for x64 |
| Command | `cl /nologo /LD /O2 /MT /W4 /DJGTESTLIB_BUILD jgtestlib.c /Fe:jgtestlib.dll` |
| jgtestlib.c SHA256 | `DA6CCD60D3C4FAF69EDC46375B21FEC1EE77BFE7E8727EF6FC53BDAFFAB9E037` |
| jgtestlib.h SHA256 | `5F4FCBC66405EAF2F738F8FECD25622148E2167AB2AA7B082C445F089AD78296` |
| DLL SHA256 | `D499CCD53B7DE76A2AA904887F76A80244B7E3E4DC6DFC033C2A2424FB1B3D62` |

`/MT` links the C runtime statically, so the DLL needs no Visual C++ redistributable.
`jgtestlib.msvc.i` next to the sources is the same compiler's `cl /P /TC jgtestlib.h`.