# libopenblas.dll — provenance

| | |
| --- | --- |
| Project | OpenBLAS (BSD-3-Clause, see `LICENSE-OpenBLAS.txt`) |
| Version | 0.3.34 (released 2026-07-16) |
| Release asset | `OpenBLAS-0.3.34-x64.zip` — the LP64 build: 32-bit `lapack_int`, plain (unsuffixed) symbol names |
| Source | <https://github.com/OpenMathLib/OpenBLAS/releases/download/v0.3.34/OpenBLAS-0.3.34-x64.zip> |
| Zip SHA256 | `E9CB6134541F36C27346D5FC5995652F060FBA227CEBBBABCBDA5A5A44D7C76B` |
| DLL SHA256 | `0486735C359A67419D8832E3C40E2837F8762BAEBCDF4B62D3B18C91F9ECE12E` |

The DLL is `bin\libopenblas.dll` from the zip, unmodified. Its PE import table names only
`KERNEL32.dll` and `msvcrt.dll` — no mingw runtime siblings (libgfortran/libquadmath/libgcc) are
needed, so this one file is the whole native dependency.

It bundles CBLAS, full LAPACK, and LAPACKE; the export set was verified against every symbol
`JGraph.Numerics` binds (see `src/JGraph.Numerics/LinearAlgebra/Native/OpenBlasNative.cs`).

To upgrade: download the new `-x64` release zip (NOT `-x64-64`, which is the ILP64 interface with
suffixed symbols), re-run the import-table and export checks, replace the DLL, and update the
hashes and version here and in `Native/OpenBlasLoader.cs`'s expectations if any.
