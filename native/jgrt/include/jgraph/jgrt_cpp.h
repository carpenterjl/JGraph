/*
 * jgraph/jgrt_cpp.h - the way the C++ shared library API into the JGraph Runtime (compiler and SDK
 * plan, stage K12): the functions jgrt.dll exports for jgraph/cpplib.hpp and jgraph/execution.hpp,
 * which a program does not call itself. Text is UTF-16. Arrays cross in JGraph's own binary form,
 * which jgraph/execution.hpp writes and reads.
 */
#ifndef JGRAPH_JGRT_CPP_H
#define JGRAPH_JGRT_CPP_H

#include <stddef.h>
#include <stdint.h>

#ifndef JGRT_API
#  ifdef __cplusplus
#    define JGRT_EXTERN_C extern "C"
#  else
#    define JGRT_EXTERN_C extern
#  endif
#  if defined(JGRT_BUILD)
#    define JGRT_API JGRT_EXTERN_C __declspec(dllexport)
#  elif defined(_WIN32)
#    define JGRT_API JGRT_EXTERN_C __declspec(dllimport)
#  else
#    define JGRT_API JGRT_EXTERN_C
#  endif
#endif

/* Text a call writes: stream 1 its output, 2 its errors and warnings it was asked to keep. */
typedef void (*jgrtCppWriteFcn)(void *context, int stream, const char16_t *text, size_t length);

/*
 * How a call ended, with its outputs or its error in the binary form; the bytes are good for the
 * call of done only. status: 0 done, 1 cancelled before it started, 2 interrupted while running,
 * 3 a MATLAB error (its record), 4 a syntax error (its record), 5 another failure (UTF-8 text),
 * 6 the runtime has ended (UTF-8 text), 7 a name the library cannot call, found once the call ran
 * (UTF-8 text: an object's first input that has no such method).
 */
typedef void (*jgrtCppDoneFcn)(void *context, int status, const unsigned char *data, size_t length);

/*
 * Starts the runtime for a C++ application, with the options as MATLAB's C++ API takes them.
 * Answers 0 when it started, 1 when an application is running already, 2 when the application
 * ended (the runtime cannot be started again in the process), 3 when it could not start (*error
 * says why; free it with jgrtCppFree).
 */
JGRT_API int jgrtCppStart(int outOfProcess, const char16_t *const *options, size_t count, char16_t **error);

/* Ends the application: every library opened in it, and the runtime. */
JGRT_API void jgrtCppStop(void);

/* Opens the archive at path, a full file name: a library's handle, or 0 with *error saying why. */
JGRT_API uint64_t jgrtCppOpen(const char16_t *path, char16_t **error);

/*
 * Opens the archive at path for a Python package (stage K13), as jgrtCppOpen opens a library: its
 * calls take and give a datetime, a duration and a dictionary as values, in forms the package's
 * runtime (the JGraph Runtime's python folder) reads and writes, rather than as objects.
 */
JGRT_API uint64_t jgrtPyOpen(const char16_t *path, char16_t **error);

/* Ends a library's handle, and the session it was opened in. */
JGRT_API void jgrtCppClose(uint64_t library);

/* Waits until none of the library's figures is on screen. */
JGRT_API void jgrtCppWaitForFigures(uint64_t library);

/*
 * Starts a call of function with nlhs outputs and the inputs given, after the calls already
 * started. output and error say whether the call's text goes to write (stream 1 and 2) or to the
 * process's standard output and error. Answers the task's handle, done called once when it ends;
 * or 0 when the library cannot call that name, with *refused saying why and done never called.
 */
JGRT_API uint64_t jgrtCppCall(uint64_t library, const char16_t *function, size_t nlhs, const unsigned char *inputs, size_t length,
                              void *context, jgrtCppWriteFcn write, int output, int error, jgrtCppDoneFcn done,
                              char16_t **refused);

/*
 * Cancels a task: one not yet started never runs (answers 1); a running one is interrupted when
 * allowInterrupt is set (answers 1), else left to finish (answers 0); a finished one answers 0.
 */
JGRT_API int jgrtCppCancel(uint64_t task, int allowInterrupt);

/* The program needs the task's handle no more. */
JGRT_API void jgrtCppForget(uint64_t task);

/* Lets go of a value the engine keeps for the program: an object array's. */
JGRT_API void jgrtCppRelease(uint64_t value);

/* Frees text the runtime gave the program. */
JGRT_API void jgrtCppFree(void *memory);

#endif /* JGRAPH_JGRT_CPP_H */
