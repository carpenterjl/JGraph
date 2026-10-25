/*
 * MATLAB Compiler: 25.2 (R2025b)
 * Date: <DATE>
 * Arguments:
 * <ARGUMENTS>
 */

#ifndef k10lib_h
#define k10lib_h 1

#if defined(__cplusplus) && !defined(mclmcrrt_h) && defined(__linux__)
#  pragma implementation "mclmcrrt.h"
#endif
#include "mclmcrrt.h"
#ifdef __cplusplus
extern "C" { // sbcheck:ok:extern_c
#endif

/* This symbol is defined in shared libraries. Define it here
 * (to nothing) in case this isn't a shared library. 
 */
#ifndef LIB_k10lib_C_API 
#define LIB_k10lib_C_API /* No special import/export declaration */
#endif

/* GENERAL LIBRARY FUNCTIONS -- START */

extern LIB_k10lib_C_API 
bool MW_CALL_CONV k10libInitializeWithHandlers(
       mclOutputHandlerFcn error_handler, 
       mclOutputHandlerFcn print_handler);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV k10libInitialize(void);
extern LIB_k10lib_C_API 
void MW_CALL_CONV k10libTerminate(void);

extern LIB_k10lib_C_API 
void MW_CALL_CONV k10libPrintStackTrace(void);

/* GENERAL LIBRARY FUNCTIONS -- END */

/* C INTERFACE -- MLX WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- START */

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10addtwo(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10echo(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10err(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10info(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10make(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10mixed(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10nargs(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10none(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10obj(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10print(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10quiet(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10two(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10val(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

/* C INTERFACE -- MLX WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- END */

/* C INTERFACE -- MLF WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- START */

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10addtwo(int nargout, mxArray** c, mxArray* a, mxArray* b);

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10echo(int nargout, mxArray** varargout, mxArray* varargin);

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10err(mxArray* kind);

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10info(int nargout, mxArray** s, mxArray* x);

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10make(int nargout, mxArray** v, mxArray* kind);

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10mixed(int nargout, mxArray** a, mxArray** varargout, mxArray* x, mxArray* varargin);

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10nargs(int nargout, mxArray** varargout, mxArray* varargin);

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10none(mxArray* x);

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10obj(int nargout, mxArray** varargout, mxArray* varargin);

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10print(mxArray* n);

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10quiet();

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10two(int nargout, mxArray** a, mxArray** b, mxArray* x);

extern LIB_k10lib_C_API bool MW_CALL_CONV mlfK10val(int nargout, mxArray** varargout, mxArray* varargin);

#ifdef __cplusplus
}
#endif
/* C INTERFACE -- MLF WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- END */

#endif
