/*
 * MATLAB Compiler: 25.2 (R2025b)
 * Date: <DATE>
 * Arguments:
 * "-B""macro_default""-W""lib:k10lib""-T""link:lib""-d""<HOME>\AppData\
 * Local\Temp\claude\D--EE-Projects-JGraph-JGraph\0bce8303-f80f-4010-b035-d2edbb
 * 754408\scratchpad\k10\b\lib""-v""k10addtwo.m""k10echo.m""k10err.m""k10info.m"
 * "k10make.m""k10mixed.m""k10nargs.m""k10none.m""k10obj.m""k10print.m""k10quiet
 * .m""k10two.m""k10val.m"
 */

#define EXPORTING_k10lib 1
#include "k10lib.h"

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
#ifndef LIB_k10lib_C_API
#define LIB_k10lib_C_API /* No special import/export declaration */
#endif

LIB_k10lib_C_API 
bool MW_CALL_CONV k10libInitializeWithHandlers(
    mclOutputHandlerFcn error_handler,
    mclOutputHandlerFcn print_handler)
{
    int bResult = 0;
    if (_mcr_inst)
        return true;
    if (!mclmcrInitialize())
        return false;
    if (!GetModuleFileName(GetModuleHandle("k10lib"), path_to_dll, _MAX_PATH))
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

LIB_k10lib_C_API 
bool MW_CALL_CONV k10libInitialize(void)
{
    return k10libInitializeWithHandlers(mclDefaultErrorHandler, mclDefaultPrintHandler);
}

LIB_k10lib_C_API 
void MW_CALL_CONV k10libTerminate(void)
{
    if (_mcr_inst)
        mclTerminateInstance(&_mcr_inst);
}

LIB_k10lib_C_API 
void MW_CALL_CONV k10libPrintStackTrace(void) 
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


LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10addtwo(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10addtwo", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10echo(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10echo", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10err(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10err", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10info(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10info", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10make(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10make", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10mixed(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10mixed", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10nargs(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10nargs", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10none(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10none", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10obj(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10obj", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10print(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10print", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10quiet(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10quiet", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10two(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10two", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlxK10val(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])
{
    return mclFeval(_mcr_inst, "k10val", nlhs, plhs, nrhs, prhs);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10addtwo(int nargout, mxArray** c, mxArray* a, mxArray* b)
{
    return mclMlfFeval(_mcr_inst, "k10addtwo", nargout, 1, 2, c, a, b);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10echo(int nargout, mxArray** varargout, mxArray* varargin)
{
    return mclMlfFeval(_mcr_inst, "k10echo", nargout, -1, -1, varargout, varargin);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10err(mxArray* kind)
{
    return mclMlfFeval(_mcr_inst, "k10err", 0, 0, 1, kind);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10info(int nargout, mxArray** s, mxArray* x)
{
    return mclMlfFeval(_mcr_inst, "k10info", nargout, 1, 1, s, x);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10make(int nargout, mxArray** v, mxArray* kind)
{
    return mclMlfFeval(_mcr_inst, "k10make", nargout, 1, 1, v, kind);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10mixed(int nargout, mxArray** a, mxArray** varargout, mxArray* x, 
                              mxArray* varargin)
{
    return mclMlfFeval(_mcr_inst, "k10mixed", nargout, -2, -2, a, varargout, x, varargin);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10nargs(int nargout, mxArray** varargout, mxArray* varargin)
{
    return mclMlfFeval(_mcr_inst, "k10nargs", nargout, -1, -1, varargout, varargin);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10none(mxArray* x)
{
    return mclMlfFeval(_mcr_inst, "k10none", 0, 0, 1, x);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10obj(int nargout, mxArray** varargout, mxArray* varargin)
{
    return mclMlfFeval(_mcr_inst, "k10obj", nargout, -1, -1, varargout, varargin);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10print(mxArray* n)
{
    return mclMlfFeval(_mcr_inst, "k10print", 0, 0, 1, n);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10quiet()
{
    return mclMlfFeval(_mcr_inst, "k10quiet", 0, 0, 0);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10two(int nargout, mxArray** a, mxArray** b, mxArray* x)
{
    return mclMlfFeval(_mcr_inst, "k10two", nargout, 2, 1, a, b, x);
}

LIB_k10lib_C_API 
bool MW_CALL_CONV mlfK10val(int nargout, mxArray** varargout, mxArray* varargin)
{
    return mclMlfFeval(_mcr_inst, "k10val", nargout, -1, -1, varargout, varargin);
}

