/*
 * mclbase.h - starting and ending the JGraph Runtime in a C program.
 *
 * Part of the JGraph Runtime (compiler and SDK plan, stage K10). A C program that calls a
 * component starts the runtime once with mclInitializeApplication, runs its work (usually through
 * mclRunMain), and ends it with mclTerminateApplication; the runtime cannot be started a second
 * time in the same process, as MATLAB Compiler's cannot. The names are MATLAB's (decision KO3);
 * the text is JGraph's own.
 */
#ifndef JGRT_MCLBASE_H
#define JGRT_MCLBASE_H

#include <stddef.h>
#include "matrix.h"

/* The calling convention of the functions a C shared library exports. */
#ifndef MW_CALL_CONV
#  if defined(_MSC_VER)
#    define MW_CALL_CONV __cdecl
#  else
#    define MW_CALL_CONV
#  endif
#endif

/* A component instance, made by one of the mclInitializeComponentInstance functions. */
typedef void *HMCRINSTANCE;

/* Where a component's output and error text go: called with each piece, answers what it wrote. */
typedef int (*mclOutputHandlerFcn)(const char *s);

/* The function mclRunMain runs. */
typedef int (*mclMainFcnType)(int argc, const char **argv);

/*
 * Starts the runtime with the given options: -nojvm, -nodisplay and -singleCompThread, and
 * -logfile followed by the file's name as the next option. Answers false when the runtime is
 * already running or has run in this process.
 */
JGRT_API bool mclInitializeApplication(const char **options, size_t count);

/* Ends the runtime and every component in it. */
JGRT_API bool mclTerminateApplication(void);

/* Whether the runtime has been started in this process (it stays true once terminated). */
JGRT_API bool mclIsMCRInitialized(void);

/* The log file -logfile named, as the runtime opened it; empty when there is none. */
JGRT_API const char *mclGetLogFileName(void);

/* Runs run_main(argc, argv) as the program's main work and answers what it returns. */
JGRT_API int mclRunMain(mclMainFcnType run_main, int argc, const char **argv);

/*
 * The MATLAB stack of the last error a call raised, one line per frame. mclGetStackTrace answers
 * the number of lines and gives an array the caller frees with mclFreeStackTrace.
 */
JGRT_API int mclGetStackTrace(char ***stack);
JGRT_API int mclFreeStackTrace(char ***stack, int nStackDepth);

/* The message of the last error a call raised; empty when none has. */
JGRT_API const char *mclGetLastErrorMessage(void);

#endif /* JGRT_MCLBASE_H */
