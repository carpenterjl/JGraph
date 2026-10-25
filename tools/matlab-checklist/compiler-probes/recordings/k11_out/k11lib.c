/*
 * MATLAB Compiler: 25.2 (R2025b)
 * Date: <DATE>
 * Arguments:
 * <ARGUMENTS>
 */

#define EXPORTING_k11lib 1
#include "k11lib.h"

static HMCRINSTANCE _mcr_inst = NULL; /* don't use nullptr; this may be either C or C++ */

#if defined( _MSC_VER) || defined(__LCC__) || defined(__MINGW64__)
#ifdef __LCC__
#undef EXTERN_C
#endif
#define NOMINMAX
#include <windows.h>
#undef interface

static char path_to_dll[_MAX_PATH];

BOOL WINAPI DllMain(HINSTANCE hInstance, DWORD dwReason, void *pv)
{
    if (dwReason == DLL_PROCESS_ATTACH)
    {
        if (GetModuleFileName(hInstance, path_to_dll, _MAX_PATH) == 0)
            return FALSE;
    }
    else if (dwReason == DLL_PROCESS_DETACH)
    {
    }
    return TRUE;
}
#endif
#ifdef __cplusplus
extern "C" { // sbcheck:ok:extern_c
#endif

static int mclDefaultPrintHandler(const char *s)
{
    return mclWrite(1 /* stdout */, s, sizeof(char)*strlen(s));
}

#ifdef __cplusplus
} /* End extern C block */
#endif

#ifdef __cplusplus
extern "C" { // sbcheck:ok:extern_c
#endif

static int mclDefaultErrorHandler(const char *s)
{
    int written = 0;
    size_t len = 0;
    len = strlen(s);
    written = mclWrite(2 /* stderr */, s, sizeof(char)*len);
    if (len > 0 && s[ len-1 ] != '\n')
        written += mclWrite(2 /* stderr */, "\n", sizeof(char));
    return written;
}

#ifdef __cplusplus
} /* End extern C block */
#endif

/* This symbol is defined in shared libraries. Define it here
 * (to nothing) in case this isn't a shared library. 
 */
#ifndef LIB_k11lib_C_API
#define LIB_k11lib_C_API /* No special import/export declaration */
#endif

LIB_k11lib_C_API 
bool MW_CALL_CONV k11libInitializeWithHandlers(
    mclOutputHandlerFcn error_handler,
    mclOutputHandlerFcn print_handler)
{
    int bResult = 0;
    if (_mcr_inst)
        return true;
    if (!mclmcrInitialize())
        return false;
    if (!GetModuleFileName(GetModuleHandle("k11lib"), path_to_dll, _MAX_PATH))
        return false;
    {
        mclCtfStream ctfStream = 
            mclGetEmbeddedCtfStream(path_to_dll);
        if (ctfStream) {
            bResult = mclInitializeComponentInstanceEmbedded(&_mcr_inst,
                                                             error_handler, 
                                                             print_handler,
                                                             ctfStream,
                                                             NULL);
            mclDestroyStream(ctfStream);
        } else {
            bResult = 0;
        }
    }  
    if (!bResult)
    return false;
    return true;
}

LIB_k11lib_C_API 
bool MW_CALL_CONV k11libInitialize(void)
{
    return k11libInitializeWithHandlers(mclDefaultErrorHandler, mclDefaultPrintHandler);
}

LIB_k11lib_C_API 
void MW_CALL_CONV k11libTerminate(void)
{
    if (_mcr_inst)
        mclTerminateInstance(&_mcr_inst);
}

LIB_k11lib_C_API 
void MW_CALL_CONV k11libPrintStackTrace(void) 
{
    char** stackTrace;
    int stackDepth = mclGetStackTrace(&stackTrace);
    int i;
    for(i=0; i<stackDepth; i++)
    {
        mclWrite(2 /* stderr */, stackTrace[i], sizeof(char)*strlen(stackTrace[i]));
        mclWrite(2 /* stderr */, "\n", sizeof(char)*strlen("\n"));
    }
    mclFreeStackTrace(&stackTrace, stackDepth);
}


LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11plain(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k11plain", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11Mixed(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k11Mixed", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11upper(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "K11upper", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11_under(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k11_under", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11long(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k11long", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11wide(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k11wide", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11_a_function_name_long_enough_to_make_generated_lines_wrap(int 
                                                                                  nlhs, 
                                                                                  mxArray 
                                                                                  *plhs[], 
                                                                                  int 
                                                                                  nrhs, 
                                                                                  mxArray 
                                                                                  *prhs[])
{
    return mclFeval(_mcr_inst, 
                  "k11_a_function_name_long_enough_to_make_generated_lines_wrap", nlhs, 
                  plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11sub(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k11sub", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11noparen(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k11noparen", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11outonly(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k11outonly", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11varin(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k11varin", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11quiet(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k11quiet", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlxK11fail(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k11fail", nlhs, plhs, nrhs, prhs);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11plain(int nargout, mxArray** y, mxArray* x)
{
    return mclMlfFeval(_mcr_inst, "k11plain", nargout, 1, 1, y, x);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11Mixed(int nargout, mxArray** a, mxArray** b, mxArray* p, mxArray* 
                              q)
{
    return mclMlfFeval(_mcr_inst, "k11Mixed", nargout, 2, 2, a, b, p, q);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11upper(int nargout, mxArray** r, mxArray* s)
{
    return mclMlfFeval(_mcr_inst, "K11upper", nargout, 1, 1, r, s);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11_under(int nargout, mxArray** o, mxArray* i)
{
    return mclMlfFeval(_mcr_inst, "k11_under", nargout, 1, 1, o, i);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11long(int nargout, mxArray** first_output, mxArray** 
                             second_output, mxArray** third_output, mxArray* alpha_input, 
                             mxArray* beta_input, mxArray* gamma_input, mxArray* 
                             delta_input, mxArray* epsilon_input)
{
    return mclMlfFeval(_mcr_inst, "k11long", nargout, 3, 5, first_output, second_output, third_output, alpha_input, beta_input, gamma_input, delta_input, epsilon_input);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11wide(int nargout, mxArray** o1, mxArray** o2, mxArray** o3, 
                             mxArray** o4, mxArray** varargout, mxArray* i1, mxArray* i2, 
                             mxArray* i3, mxArray* i4, mxArray* i5, mxArray* i6, mxArray* 
                             i7, mxArray* i8, mxArray* varargin)
{
    return mclMlfFeval(_mcr_inst, "k11wide", nargout, -5, -9, o1, o2, o3, o4, varargout, i1, i2, i3, i4, i5, i6, i7, i8, varargin);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11_a_function_name_long_enough_to_make_generated_lines_wrap(int 
                                                                                  nargout, 
                                                                                  mxArray** 
                                                                                  result, 
                                                                                  mxArray* 
                                                                                  value)
{
    return mclMlfFeval(_mcr_inst, "k11_a_function_name_long_enough_to_make_generated_lines_wrap", nargout, 1, 1, result, value);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11sub(int nargout, mxArray** y, mxArray* x)
{
    return mclMlfFeval(_mcr_inst, "k11sub", nargout, 1, 1, y, x);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11noparen()
{
    return mclMlfFeval(_mcr_inst, "k11noparen", 0, 0, 0);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11outonly(int nargout, mxArray** a, mxArray** varargout)
{
    return mclMlfFeval(_mcr_inst, "k11outonly", nargout, -2, 0, a, varargout);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11varin(mxArray* varargin)
{
    return mclMlfFeval(_mcr_inst, "k11varin", 0, 0, -1, varargin);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11quiet()
{
    return mclMlfFeval(_mcr_inst, "k11quiet", 0, 0, 0);
}

LIB_k11lib_C_API 
bool MW_CALL_CONV mlfK11fail(mxArray* message)
{
    return mclMlfFeval(_mcr_inst, "k11fail", 0, 0, 1, message);
}

