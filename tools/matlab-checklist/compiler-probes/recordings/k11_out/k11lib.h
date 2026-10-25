/*
 * MATLAB Compiler: 25.2 (R2025b)
 * Date: <DATE>
 * Arguments:
 * <ARGUMENTS>
 */

#ifndef k11lib_h
#define k11lib_h 1

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
#ifndef LIB_k11lib_C_API 
#define LIB_k11lib_C_API /* No special import/export declaration */
#endif

/* GENERAL LIBRARY FUNCTIONS -- START */

extern LIB_k11lib_C_API 
bool MW_CALL_CONV k11libInitializeWithHandlers(
       mclOutputHandlerFcn error_handler, 
       mclOutputHandlerFcn print_handler);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV k11libInitialize(void);
extern LIB_k11lib_C_API 
void MW_CALL_CONV k11libTerminate(void);

extern LIB_k11lib_C_API 
void MW_CALL_CONV k11libPrintStackTrace(void);

/* GENERAL LIBRARY FUNCTIONS -- END */

/* C INTERFACE -- MLX WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- START */

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11plain(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11Mixed(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11upper(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11_under(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11long(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11wide(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11_a_function_name_long_enough_to_make_generated_lines_wrap(int 
                                                                                  nlhs, 
                                                                                  mxArray 
                                                                                  *plhs[], 
                                                                                  int 
                                                                                  nrhs, 
                                                                                  mxArray 
                                                                                  *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11sub(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11noparen(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11outonly(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11varin(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11quiet(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

extern LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11fail(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

/* C INTERFACE -- MLX WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- END */

/* C INTERFACE -- MLF WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- START */

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11plain(int nargout, mxArray** y, mxArray* x);

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11Mixed(int nargout, mxArray** a, mxArray** b, mxArray* p, mxArray* q);

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11upper(int nargout, mxArray** r, mxArray* s);

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11_under(int nargout, mxArray** o, mxArray* i);

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11long(int nargout, mxArray** first_output, mxArray** second_output, mxArray** third_output, mxArray* alpha_input, mxArray* beta_input, mxArray* gamma_input, mxArray* delta_input, mxArray* epsilon_input);

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11wide(int nargout, mxArray** o1, mxArray** o2, mxArray** o3, mxArray** o4, mxArray** varargout, mxArray* i1, mxArray* i2, mxArray* i3, mxArray* i4, mxArray* i5, mxArray* i6, mxArray* i7, mxArray* i8, mxArray* varargin);

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11_a_function_name_long_enough_to_make_generated_lines_wrap(int nargout, mxArray** result, mxArray* value);

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11sub(int nargout, mxArray** y, mxArray* x);

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11noparen();

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11outonly(int nargout, mxArray** a, mxArray** varargout);

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11varin(mxArray* varargin);

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11quiet();

extern LIB_k11lib_C_API bool MW_CALL_CONV mlfK11fail(mxArray* message);

#ifdef __cplusplus
}
#endif
/* C INTERFACE -- MLF WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- END */

#endif
