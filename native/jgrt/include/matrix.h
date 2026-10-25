/*
 * matrix.h - the mxArray C API of the JGraph Runtime.
 *
 * Part of the JGraph Runtime (compiler and SDK plan, stage K10). An mxArray is a MATLAB value a C
 * program holds: numeric, logical, char, cell, struct or sparse data it makes and reads itself,
 * or a value it was handed by a component (an object, a string, a function handle) that it can
 * pass back and read the properties of. The function names and their behaviour are MATLAB's, so
 * C code written against MATLAB Compiler's runtime compiles against this one unchanged (decision
 * KO3); the text of this header is JGraph's own.
 *
 * Complex numbers: a compilation unit uses one of two models, chosen as MATLAB's mex and mbuild
 * choose it.
 *   - separate: real and imaginary parts in two arrays, read and written with mxGetPr, mxGetPi,
 *     mxGetData and mxGetImagData. mbuild's default (it defines MATLAB_DEFAULT_RELEASE=R2017b),
 *     and what MATLAB_MEXCMD_RELEASE or MATLAB_MEXSRC_RELEASE set to R2017b give.
 *   - interleaved: each element's parts side by side (mxComplexDouble and its kin), read and
 *     written with the typed accessors (mxGetDoubles, mxGetComplexDoubles, ...). What R2018a and
 *     later name, and what a unit gets when none of those macros is defined.
 * An array made in one model can be read in the other; the runtime rearranges its storage the
 * first time it is asked for the other layout, so a pointer from before then is stale after it.
 *
 * Memory: every mxArray, and every buffer mxMalloc, mxCalloc, mxRealloc, mxArrayToString and
 * mxArrayToUTF8String return, belongs to the caller until it is given away (mxSetCell, mxSetField,
 * mxSetPr and the like take ownership) or freed with mxDestroyArray or mxFree. Nothing is freed
 * behind the caller's back.
 */
#ifndef JGRT_MATRIX_H
#define JGRT_MATRIX_H

#include <stdlib.h>
#include <stddef.h>
#include <assert.h>
#include "tmwtypes.h"

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

/* ---- Which complex model this unit uses ------------------------------------------------------ */

#if defined(MX_COMPAT_32)
#error "The JGraph Runtime has no 32-bit compatibility API: build without MX_COMPAT_32."
#endif

/* A release name as the model it chose: R2017b the separate one, R2018a and later interleaved. */
#define JGRT_MODEL_OF_R2017b 700
#define JGRT_MODEL_OF_R2018a 800
#define JGRT_MODEL_OF_R2018b 800
#define JGRT_MODEL_OF_R2019a 800
#define JGRT_MODEL_OF_R2019b 800
#define JGRT_MODEL_OF_R2020a 800
#define JGRT_MODEL_OF_R2020b 800
#define JGRT_MODEL_OF_R2021a 800
#define JGRT_MODEL_OF_R2021b 800
#define JGRT_MODEL_OF_R2022a 800
#define JGRT_MODEL_OF_R2022b 800
#define JGRT_MODEL_OF_R2023a 800
#define JGRT_MODEL_OF_R2023b 800
#define JGRT_MODEL_OF_R2024a 800
#define JGRT_MODEL_OF_R2024b 800
#define JGRT_MODEL_OF_R2025a 800
#define JGRT_MODEL_OF_R2025b 800
#define JGRT_MODEL_OF_R2026a 800
#define JGRT_MODEL_OF_R2026b 800
#define JGRT_MODEL_OF_(release) JGRT_MODEL_OF_##release
#define JGRT_MODEL_OF(release) JGRT_MODEL_OF_(release)

#if defined(MATLAB_MEXCMD_RELEASE)
#  define JGRT_MODEL JGRT_MODEL_OF(MATLAB_MEXCMD_RELEASE)
#elif defined(MATLAB_MEXSRC_RELEASE)
#  define JGRT_MODEL JGRT_MODEL_OF(MATLAB_MEXSRC_RELEASE)
#elif defined(MATLAB_DEFAULT_RELEASE)
#  define JGRT_MODEL JGRT_MODEL_OF(MATLAB_DEFAULT_RELEASE)
#else
#  define JGRT_MODEL 800
#endif

#if JGRT_MODEL != 700 && JGRT_MODEL != 800
#error "The release named by MATLAB_MEXCMD_RELEASE, MATLAB_MEXSRC_RELEASE or MATLAB_DEFAULT_RELEASE is not one the JGraph Runtime knows (R2017b to R2026b)."
#endif

#if defined(TARGET_API_VERSION) && TARGET_API_VERSION != JGRT_MODEL
#error "TARGET_API_VERSION names a different complex model than the release macros do."
#endif
#ifndef TARGET_API_VERSION
#define TARGET_API_VERSION JGRT_MODEL
#endif

/* The version numbers code tests to learn which model it was built in. */
#define MX_CURRENT_API_VER 0x08000000
#define MX_API_VER MX_CURRENT_API_VER
#define MX_LAST_32BIT_VER 0x07000000
#define MX_LAST_SEPARATE_COMPLEX_VER 0x07300000
#if JGRT_MODEL == 700
#  define MX_TARGET_API_VER MX_LAST_SEPARATE_COMPLEX_VER
#else
#  define MX_TARGET_API_VER MX_CURRENT_API_VER
#endif
#define MX_HAS_64BIT_ARRAY_DIMS (MX_TARGET_API_VER > MX_LAST_32BIT_VER)
#define MX_HAS_INTERLEAVED_COMPLEX (MX_TARGET_API_VER > MX_LAST_SEPARATE_COMPLEX_VER)

/* ---- Types --------------------------------------------------------------------------------- */

/* A MATLAB value. Its layout is the runtime's own: reach it only through these functions. */
typedef struct mxArray_tag mxArray;

/* The shape of a MEX function, kept for code that names the type. */
typedef void (*mxFunctionPtr)(int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[]);

/* The longest name a variable or a field may have. */
#define mxMAXNAM TMW_NAME_LENGTH_MAX

typedef bool mxLogical;
typedef CHAR16_T mxChar;

#if !defined(__cplusplus) || __cplusplus < 201103L
typedef enum
#else
enum mxClassID : int
#endif
{
    mxUNKNOWN_CLASS = 0,
    mxCELL_CLASS,
    mxSTRUCT_CLASS,
    mxLOGICAL_CLASS,
    mxCHAR_CLASS,
    mxVOID_CLASS,
    mxDOUBLE_CLASS,
    mxSINGLE_CLASS,
    mxINT8_CLASS,
    mxUINT8_CLASS,
    mxINT16_CLASS,
    mxUINT16_CLASS,
    mxINT32_CLASS,
    mxUINT32_CLASS,
    mxINT64_CLASS,
    mxUINT64_CLASS,
    mxFUNCTION_CLASS,
    mxOPAQUE_CLASS,
    mxOBJECT_CLASS,
    mxINDEX_CLASS = mxUINT64_CLASS
}
#if !defined(__cplusplus) || __cplusplus < 201103L
mxClassID
#endif
;

typedef enum { mxREAL, mxCOMPLEX } mxComplexity;

typedef double mxDouble;
typedef float mxSingle;
typedef int8_T mxInt8;
typedef uint8_T mxUint8;
typedef int16_T mxInt16;
typedef uint16_T mxUint16;
typedef int32_T mxInt32;
typedef uint32_T mxUint32;
typedef int64_T mxInt64;
typedef uint64_T mxUint64;

#if MX_HAS_INTERLEAVED_COMPLEX
typedef struct { mxDouble real, imag; } mxComplexDouble;
typedef struct { mxSingle real, imag; } mxComplexSingle;
typedef struct { mxInt8 real, imag; } mxComplexInt8;
typedef struct { mxUint8 real, imag; } mxComplexUint8;
typedef struct { mxInt16 real, imag; } mxComplexInt16;
typedef struct { mxUint16 real, imag; } mxComplexUint16;
typedef struct { mxInt32 real, imag; } mxComplexInt32;
typedef struct { mxUint32 real, imag; } mxComplexUint32;
typedef struct { mxInt64 real, imag; } mxComplexInt64;
typedef struct { mxUint64 real, imag; } mxComplexUint64;
#endif

/* ---- The functions whose meaning depends on the complex model -------------------------------- */

#if !MX_HAS_INTERLEAVED_COMPLEX
#  define mxGetPr mxGetPr_730
#  define mxSetPr mxSetPr_730
#  define mxGetData mxGetData_730
#  define mxSetData mxSetData_730
#  define mxGetElementSize mxGetElementSize_730
#  define mxGetPi mxGetPi_730
#  define mxSetPi mxSetPi_730
#  define mxGetImagData mxGetImagData_730
#  define mxSetImagData mxSetImagData_730
#else
/* The separate model's parts have no meaning here: name them and the build says so. */
#  define mxGetPi mxGetPi_needs_the_separate_complex_model
#  define mxSetPi mxSetPi_needs_the_separate_complex_model
#  define mxGetImagData mxGetImagData_needs_the_separate_complex_model
#  define mxSetImagData mxSetImagData_needs_the_separate_complex_model
#endif

/* ---- Memory -------------------------------------------------------------------------------- */

JGRT_API void *mxMalloc(size_t n);
JGRT_API void *mxCalloc(size_t n, size_t size);
JGRT_API void *mxRealloc(void *ptr, size_t size);
JGRT_API void mxFree(void *ptr);

/* ---- Making and destroying arrays ------------------------------------------------------------ */

JGRT_API mxArray *mxCreateDoubleMatrix(mwSize m, mwSize n, mxComplexity complexity);
JGRT_API mxArray *mxCreateDoubleScalar(double value);
JGRT_API mxArray *mxCreateNumericMatrix(mwSize m, mwSize n, mxClassID classid, mxComplexity complexity);
JGRT_API mxArray *mxCreateNumericArray(mwSize ndim, const mwSize *dims, mxClassID classid, mxComplexity complexity);
JGRT_API mxArray *mxCreateUninitNumericMatrix(size_t m, size_t n, mxClassID classid, mxComplexity complexity);
JGRT_API mxArray *mxCreateUninitNumericArray(size_t ndim, size_t *dims, mxClassID classid, mxComplexity complexity);
JGRT_API mxArray *mxCreateLogicalScalar(bool value);
JGRT_API mxArray *mxCreateLogicalMatrix(mwSize m, mwSize n);
JGRT_API mxArray *mxCreateLogicalArray(mwSize ndim, const mwSize *dims);
JGRT_API mxArray *mxCreateString(const char *str);
JGRT_API mxArray *mxCreateStringFromNChars(const char *str, mwSize n);
JGRT_API mxArray *mxCreateCharMatrixFromStrings(mwSize m, const char **str);
JGRT_API mxArray *mxCreateCharArray(mwSize ndim, const mwSize *dims);
JGRT_API mxArray *mxCreateCellMatrix(mwSize m, mwSize n);
JGRT_API mxArray *mxCreateCellArray(mwSize ndim, const mwSize *dims);
JGRT_API mxArray *mxCreateStructMatrix(mwSize m, mwSize n, int nfields, const char **fieldnames);
JGRT_API mxArray *mxCreateStructArray(mwSize ndim, const mwSize *dims, int nfields, const char **fieldnames);
JGRT_API mxArray *mxCreateSparse(mwSize m, mwSize n, mwSize nzmax, mxComplexity complexity);
JGRT_API mxArray *mxCreateSparseLogicalMatrix(mwSize m, mwSize n, mwSize nzmax);
JGRT_API mxArray *mxDuplicateArray(const mxArray *in);
JGRT_API void mxDestroyArray(mxArray *pa);

/* ---- Class and shape ------------------------------------------------------------------------- */

JGRT_API mxClassID mxGetClassID(const mxArray *pa);
JGRT_API const char *mxGetClassName(const mxArray *pa);
JGRT_API bool mxIsClass(const mxArray *pa, const char *name);
JGRT_API mwSize mxGetNumberOfDimensions(const mxArray *pa);
JGRT_API const mwSize *mxGetDimensions(const mxArray *pa);
JGRT_API size_t mxGetM(const mxArray *pa);
JGRT_API size_t mxGetN(const mxArray *pa);
JGRT_API size_t mxGetNumberOfElements(const mxArray *pa);
JGRT_API size_t mxGetElementSize(const mxArray *pa);
JGRT_API mwIndex mxCalcSingleSubscript(const mxArray *pa, mwSize nsubs, const mwIndex *subs);
JGRT_API void mxSetM(mxArray *pa, mwSize m);
JGRT_API void mxSetN(mxArray *pa, mwSize n);
JGRT_API int mxSetDimensions(mxArray *pa, const mwSize *pdims, mwSize ndims);

JGRT_API bool mxIsNumeric(const mxArray *pa);
JGRT_API bool mxIsCell(const mxArray *pa);
JGRT_API bool mxIsLogical(const mxArray *pa);
JGRT_API bool mxIsChar(const mxArray *pa);
JGRT_API bool mxIsStruct(const mxArray *pa);
JGRT_API bool mxIsSparse(const mxArray *pa);
JGRT_API bool mxIsComplex(const mxArray *pa);
JGRT_API bool mxIsDouble(const mxArray *pa);
JGRT_API bool mxIsSingle(const mxArray *pa);
JGRT_API bool mxIsInt8(const mxArray *pa);
JGRT_API bool mxIsUint8(const mxArray *pa);
JGRT_API bool mxIsInt16(const mxArray *pa);
JGRT_API bool mxIsUint16(const mxArray *pa);
JGRT_API bool mxIsInt32(const mxArray *pa);
JGRT_API bool mxIsUint32(const mxArray *pa);
JGRT_API bool mxIsInt64(const mxArray *pa);
JGRT_API bool mxIsUint64(const mxArray *pa);
JGRT_API bool mxIsEmpty(const mxArray *pa);
JGRT_API bool mxIsScalar(const mxArray *pa);
JGRT_API bool mxIsOpaque(const mxArray *pa);
JGRT_API bool mxIsFunctionHandle(const mxArray *pa);
JGRT_API bool mxIsObject(const mxArray *pa);
JGRT_API bool mxIsLogicalScalar(const mxArray *pa);
JGRT_API bool mxIsLogicalScalarTrue(const mxArray *pa);
JGRT_API bool mxIsFromGlobalWS(const mxArray *pa);
JGRT_API void mxSetFromGlobalWS(mxArray *pa, bool global);
JGRT_API int mxGetUserBits(const mxArray *pa);
JGRT_API void mxSetUserBits(mxArray *pa, int value);

/* ---- Data ---------------------------------------------------------------------------------- */

JGRT_API double mxGetScalar(const mxArray *pa);
JGRT_API mxLogical *mxGetLogicals(const mxArray *pa);
JGRT_API mxChar *mxGetChars(const mxArray *pa);

#if MX_HAS_INTERLEAVED_COMPLEX
JGRT_API void *mxGetData(const mxArray *pa);
JGRT_API void mxSetData(mxArray *pa, void *newdata);
JGRT_API double *mxGetPr(const mxArray *pa);
JGRT_API void mxSetPr(mxArray *pa, double *newdata);

#define JGRT_ACCESSORS(Name)                                                \
    JGRT_API mx##Name *mxGet##Name##s(const mxArray *pa);                   \
    JGRT_API int mxSet##Name##s(mxArray *pa, mx##Name *data);               \
    JGRT_API mxComplex##Name *mxGetComplex##Name##s(const mxArray *pa);     \
    JGRT_API int mxSetComplex##Name##s(mxArray *pa, mxComplex##Name *data)
JGRT_ACCESSORS(Double);
JGRT_ACCESSORS(Single);
JGRT_ACCESSORS(Int8);
JGRT_ACCESSORS(Uint8);
JGRT_ACCESSORS(Int16);
JGRT_ACCESSORS(Uint16);
JGRT_ACCESSORS(Int32);
JGRT_ACCESSORS(Uint32);
JGRT_ACCESSORS(Int64);
JGRT_ACCESSORS(Uint64);
#undef JGRT_ACCESSORS
JGRT_API int mxMakeArrayReal(mxArray *pa);
JGRT_API int mxMakeArrayComplex(mxArray *pa);
#else
JGRT_API void *mxGetData_730(const mxArray *pa);
JGRT_API void mxSetData_730(mxArray *pa, void *newdata);
JGRT_API double *mxGetPr_730(const mxArray *pa);
JGRT_API void mxSetPr_730(mxArray *pa, double *newdata);
JGRT_API double *mxGetPi_730(const mxArray *pa);
JGRT_API void mxSetPi_730(mxArray *pa, double *pi);
JGRT_API void *mxGetImagData_730(const mxArray *pa);
JGRT_API void mxSetImagData_730(mxArray *pa, void *newdata);
#endif

/* ---- Sparse arrays ------------------------------------------------------------------------- */

JGRT_API mwIndex *mxGetIr(const mxArray *pa);
JGRT_API void mxSetIr(mxArray *pa, mwIndex *newir);
JGRT_API mwIndex *mxGetJc(const mxArray *pa);
JGRT_API void mxSetJc(mxArray *pa, mwIndex *newjc);
JGRT_API mwSize mxGetNzmax(const mxArray *pa);
JGRT_API void mxSetNzmax(mxArray *pa, mwSize nzmax);

/* ---- Text ---------------------------------------------------------------------------------- */

JGRT_API int mxGetString(const mxArray *pa, char *buf, mwSize buflen);
JGRT_API void mxGetNChars(const mxArray *pa, char *buf, mwSize nChars);
JGRT_API char *mxArrayToString(const mxArray *pa);
JGRT_API char *mxArrayToUTF8String(const mxArray *pa);

/* ---- Cells --------------------------------------------------------------------------------- */

JGRT_API mxArray *mxGetCell(const mxArray *pa, mwIndex i);
JGRT_API void mxSetCell(mxArray *pa, mwIndex i, mxArray *value);

/* ---- Structs ------------------------------------------------------------------------------- */

JGRT_API int mxGetNumberOfFields(const mxArray *pa);
JGRT_API const char *mxGetFieldNameByNumber(const mxArray *pa, int n);
JGRT_API int mxGetFieldNumber(const mxArray *pa, const char *name);
JGRT_API mxArray *mxGetField(const mxArray *pa, mwIndex i, const char *fieldname);
JGRT_API void mxSetField(mxArray *pa, mwIndex i, const char *fieldname, mxArray *value);
JGRT_API mxArray *mxGetFieldByNumber(const mxArray *pa, mwIndex i, int fieldnum);
JGRT_API void mxSetFieldByNumber(mxArray *pa, mwIndex i, int fieldnum, mxArray *value);
JGRT_API int mxAddField(mxArray *pa, const char *fieldname);
JGRT_API void mxRemoveField(mxArray *pa, int field);

/* ---- Objects a component returned ------------------------------------------------------------ */

JGRT_API mxArray *mxGetProperty(const mxArray *pa, const mwIndex i, const char *propname);
JGRT_API void mxSetProperty(mxArray *pa, mwIndex i, const char *propname, const mxArray *value);

/* ---- IEEE values --------------------------------------------------------------------------- */

JGRT_API double mxGetEps(void);
JGRT_API double mxGetInf(void);
JGRT_API double mxGetNaN(void);
JGRT_API bool mxIsFinite(double x);
JGRT_API bool mxIsInf(double x);
JGRT_API bool mxIsNaN(double x);

/* Outside a MEX file an assertion is the C library's. */
#define mxAssert(test, message) assert(test)
#define mxAssertS(test, message) assert(test)

#endif /* JGRT_MATRIX_H */
