# jgrt.dll and jgrt.lib

Built by `native\jgrt\build.ps1` from `native\jgrt\src` and `native\jgrt\include` (compiler and SDK plan, stages K10 and K12).

- Compiler: Microsoft (R) C/C++ Optimizing Compiler Version 19.44.35229 for x64 (x64), `/O2 /MT /EHsc /W4 /WX /std:c++17 /Brepro`
- nethost: `8.0.31` apphost pack, linked statically

- `jgrt.dll`: 315904 bytes, SHA-256 `7e5e2784410cc8feeca4cbb78cc126043c3db0bd52e8d79e07d6a6502d0171bb`
- `jgrt.lib`: 38122 bytes, SHA-256 `0e454b0e3f6968456397e8925989af9eaf972141e436bb6f3d4a31009e85ac26`
- `tests\JGraph.Tests\Runtime\native\win-x64\jgrtclient.exe` (NativeBridgeTests' C program): 160768 bytes, SHA-256 `0e38a9cf68bff4b554ce75e1e68791cec3518be6e84127c6c54566561f771299`
