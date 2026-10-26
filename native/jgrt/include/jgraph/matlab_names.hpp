/*
 * jgraph/matlab_names.hpp - MathWorks' namespace name for JGraph's (compiler and SDK plan, stage K12,
 * decision KO3). Every MathWorks-named header (MatlabDataArray.hpp, MatlabCppSharedLib.hpp,
 * MatlabExecutionInterface.hpp, MatlabTypesInterface.hpp and MatlabDataArray/) includes it, so a
 * program written for MATLAB's runtime, and the strongly typed header a build generates, name
 * matlab::data, matlab::execution and matlab::cpplib and are given JGraph's. A program that says
 * "namespace matlab = jgraph;" itself declares the same alias again, which C++ allows; one that
 * includes only the jgraph headers has the jgraph namespace alone.
 */
#ifndef JGRAPH_MATLAB_NAMES_HPP
#define JGRAPH_MATLAB_NAMES_HPP

namespace jgraph {
}

namespace matlab = jgraph;

#endif /* JGRAPH_MATLAB_NAMES_HPP */
