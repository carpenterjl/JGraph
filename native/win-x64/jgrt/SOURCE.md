# jgrt.dll and jgrt.lib

Built by `native\jgrt\build.ps1` from `native\jgrt\src` and `native\jgrt\include` (compiler and SDK plan, stages K10 and K12).

- Compiler: Microsoft (R) C/C++ Optimizing Compiler Version 19.44.35229 for x64 (x64), `/O2 /MT /EHsc /W4 /WX /std:c++17 /Brepro`
- nethost: `8.0.31` apphost pack, linked statically

- `jgrt.dll`: 377344 bytes, SHA-256 `31b233e0eba4fcfa34d367b0d91cd8428c90d016b9689c4ce67aad2775b41df2`
- `jgrt.lib`: 39490 bytes, SHA-256 `090997e0918b0c0f9aa7062ad12178956d946f627298eb966afd7625828b7bc5`
- `tests\JGraph.Tests\Runtime\native\win-x64\jgrtclient.exe` (NativeBridgeTests' C program): 160768 bytes, SHA-256 `b368ff057519541986bf29eaf74ef386077b30d6c5054af17f2478cc235da3d9`
