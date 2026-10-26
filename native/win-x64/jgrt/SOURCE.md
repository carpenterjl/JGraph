# jgrt.dll and jgrt.lib

Built by `native\jgrt\build.ps1` from `native\jgrt\src` and `native\jgrt\include` (compiler and SDK plan, stages K10 and K12).

- Compiler: Microsoft (R) C/C++ Optimizing Compiler Version 19.44.35229 for x64 (x64), `/O2 /MT /EHsc /W4 /WX /std:c++17 /Brepro`
- nethost: `8.0.31` apphost pack, linked statically

- `jgrt.dll`: 316416 bytes, SHA-256 `3fddeec47a5ee16b3d82d7798b0d24993f51f85fc6dea171ab2a127798ca6eb4`
- `jgrt.lib`: 38294 bytes, SHA-256 `3b7c13d8df136fd8adfcf8a6ae5713bd587f588f84086409e4566c792ea50f71`
- `tests\JGraph.Tests\Runtime\native\win-x64\jgrtclient.exe` (NativeBridgeTests' C program): 160768 bytes, SHA-256 `cd382e2b0e430ea6be6145cf0a31a9d458ea0795aa1737b4de01b1eee65d7509`
