# jgrt.dll and jgrt.lib

Built by `native\jgrt\build.ps1` from `native\jgrt\src` and `native\jgrt\include` (compiler and SDK plan, stages K10 and K12).

- Compiler: Microsoft (R) C/C++ Optimizing Compiler Version 19.44.35229 for x64 (x64), `/O2 /MT /EHsc /W4 /WX /std:c++17 /Brepro`
- nethost: `8.0.31` apphost pack, linked statically

- `jgrt.dll`: 373760 bytes, SHA-256 `d3a14fd0cbcc8e491e0a868703ceee422db977aa6e457189add7cc0084fe358b`
- `jgrt.lib`: 39282 bytes, SHA-256 `a814c757e5815cd406654b108519cb6a8a6c4aeb2e75a30083877c03340cab57`
- `tests\JGraph.Tests\Runtime\native\win-x64\jgrtclient.exe` (NativeBridgeTests' C program): 160768 bytes, SHA-256 `a255253a1760539a55eb9660b627ae026cf263f343b44ab421add0d6e5a9bc18`
