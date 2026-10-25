/*
 * mclmcr.h - a component's instances and calls, as a C shared library makes and uses them.
 *
 * Part of the JGraph Runtime (compiler and SDK plan, stage K10). A C shared library built by
 * mcc -W lib carries its component's archive; its Initialize function finds the archive with
 * mclGetEmbeddedCtfStream and makes an instance from it, and each of its mlx and mlf functions
 * calls one of the component's functions through mclFeval or mclMlfFeval. The names are MATLAB's
 * (decision KO3); the text is JGraph's own.
 */
#ifndef JGRT_MCLMCR_H
#define JGRT_MCLMCR_H

#include <stdarg.h>
#include <stddef.h>
#include "mclbase.h"

/* A component's archive, found but not yet opened. */
typedef struct mclCtfStream_tag *mclCtfStream;

/* What a component was built as, as a library built with its archive beside it names itself (stage K11). */
typedef enum
{
    ExeTarget,
    LibTarget,
    MexTarget,
    SfunTarget,
    AnyTarget
} mccTargetType;

/* Prepares the runtime's library for use. Answers true; calling it again does nothing. */
JGRT_API bool mclmcrInitialize(void);

/*
 * The archive carried by the file at path (a library or a program with the archive appended, or
 * a .ctf); NULL when the file carries none. path is the file's name, as GetModuleFileName gives it.
 */
JGRT_API mclCtfStream mclGetEmbeddedCtfStream(void *path);
JGRT_API void mclDestroyStream(mclCtfStream stream);

/*
 * Makes an instance of the component an archive holds, with its output going to print_handler
 * and its errors to error_handler (each may be NULL, for standard output and standard error).
 * Instances of one archive share the component's session. session_key is accepted and unused.
 */
JGRT_API bool mclInitializeComponentInstanceEmbedded(HMCRINSTANCE *inst,
                                                    mclOutputHandlerFcn error_handler,
                                                    mclOutputHandlerFcn print_handler,
                                                    mclCtfStream ctfStream,
                                                    const char *session_key);

/* The same, from a .ctf file of its own. */
JGRT_API bool mclInitializeComponentInstanceCtfFileToCache(HMCRINSTANCE *inst,
                                                          mclOutputHandlerFcn error_handler,
                                                          mclOutputHandlerFcn print_handler,
                                                          const char *ctfFileLocation);

/*
 * The same, from component_name.ctf in the folder of the file path_to_component: what a library
 * built with mcc -C calls, its archive written beside it (stage K11). ttype and session_key are
 * accepted and unused.
 */
JGRT_API bool mclInitializeComponentInstanceNonEmbeddedStandalone(HMCRINSTANCE *inst,
                                                                 const char *path_to_component,
                                                                 const char *component_name,
                                                                 mccTargetType ttype,
                                                                 mclOutputHandlerFcn error_handler,
                                                                 mclOutputHandlerFcn print_handler,
                                                                 const char *session_key);

/* Retires an instance and sets *inst to NULL; the component's session lasts until the runtime ends. */
JGRT_API bool mclTerminateInstance(HMCRINSTANCE *inst);

/*
 * Calls the component's function name with nrhs inputs, asking for nlhs outputs, as
 * [plhs{1:nlhs}] = name(prhs{1:nrhs}) would. The inputs stay the caller's; each output is a new
 * array the caller owns. On an error it answers false, having sent the error's report to the
 * instance's error handler; mclGetStackTrace then gives its stack.
 */
JGRT_API bool mclFeval(HMCRINSTANCE inst, const char *name, int nlhs, mxArray **plhs, int nrhs, mxArray **prhs);

/*
 * The call an mlf function makes: fnout output pointers (mxArray **) then fnin inputs (mxArray *)
 * follow. A negative count means -count arguments of which the last is a cell array: the
 * outputs past the named ones (varargout), or the inputs past them (varargin). nargout outputs
 * are asked for; each output pointer receives a new array, or is left alone when not asked for.
 */
JGRT_API bool mclMlfFeval(HMCRINSTANCE inst, const char *name, int nargout, int fnout, int fnin, ...);
JGRT_API bool mclMlfVFeval(HMCRINSTANCE inst, const char *name, int nargout, int fnout, int fnin, va_list ap);

/* Writes count bytes to standard output (fd 1) or standard error (fd 2); answers the bytes written. */
JGRT_API int mclWrite(int fd, const void *ptr, size_t count);

/* Waits until none of the instance's component's figures is on screen. */
JGRT_API void mclWaitForFiguresToDie(HMCRINSTANCE inst);

#endif /* JGRT_MCLMCR_H */
