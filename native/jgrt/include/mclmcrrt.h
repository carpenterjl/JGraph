/*
 * mclmcrrt.h - the JGraph Runtime's C API, all of it: the header a C shared library built by
 * mcc -W lib and its clients include.
 *
 * Part of the JGraph Runtime (compiler and SDK plan, stage K10). Link with jgrt.lib, and put the
 * runtime's runtime\win64 folder (where jgrt.dll lives) on the program's PATH, as MATLAB
 * Compiler's runtime puts its own there; jgrt.dll finds the rest of the runtime from where it is.
 * The names are MATLAB's (decision KO3); the text is JGraph's own.
 */
#ifndef JGRT_MCLMCRRT_H
#define JGRT_MCLMCRRT_H

/* MATLAB's own header brings windows.h in on Windows, and code built against it relies on that. */
#ifdef _WIN32
#include <windows.h>
#endif

#include "matrix.h"
#include "mclbase.h"
#include "mclmcr.h"

#endif /* JGRT_MCLMCRRT_H */
