
1. Prerequisites for Deployment 

Verify that the JGraph Runtime (0.2.3) is installed, with its runtime\win64 folder
on the PATH, where a program finds jgrt.dll, which runs the library's archive.
A program compiles with the runtime's include folder, where MatlabCppSharedLib.hpp and
the jgraph headers are, and links with the runtime's lib\win64\jgrt.lib (on the computer
that builds it, JGraph's extern\include and extern\lib\win64\microsoft hold the same).

2. Files to Deploy and Package

Files to Package for the Generic Interface
==========================================
-k12lib.ctf (the deployable archive, which initMATLABLibrary opens) 
-k12libv2.hpp (the strongly typed interface)
-This readme file

3. Definitions

The generic interface keeps MathWorks' class and function names in the jgraph namespace, which
the runtime's MathWorks-named headers also call matlab, so a program written for MATLAB's runtime
compiles against JGraph's as it is. The strongly typed interface calls the functions and classes
in k12lib.ctf by their MATLAB names.


