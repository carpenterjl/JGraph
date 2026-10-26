/*
 * MatlabCppSharedLib.hpp - the header a client of a C++ shared library built for the MATLAB Data
 * API includes, as the JGraph Runtime gives it (compiler and SDK plan, stage K12): the Data API in
 * jgraph::data, calls in jgraph::execution, the application and libraries in jgraph::cpplib. Link
 * with jgrt.lib, and put the runtime's runtime\win64 folder (where jgrt.dll lives) on the program's
 * PATH. MathWorks' names, JGraph's namespace (decision KO3): this header, like every MathWorks-named
 * one, names jgraph matlab as well (jgraph/matlab_names.hpp), so a client written for MATLAB's
 * runtime compiles as it is. See jgraph/cpplib.hpp.
 */
#ifndef JGRAPH_MATLABCPPSHAREDLIB_HPP
#define JGRAPH_MATLABCPPSHAREDLIB_HPP
#include "jgraph/cpplib.hpp"
#include "jgraph/matlab_names.hpp"
#endif
