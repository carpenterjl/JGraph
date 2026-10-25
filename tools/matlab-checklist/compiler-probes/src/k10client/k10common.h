/*
 * k10common.h: what the K10 probe clients share (compiler and SDK plan, stage K10).
 *
 * The clients are built twice from the same source: against R2025b's extern\include and
 * mclmcrrt.lib, and against the JGraph Runtime's include and jgrt.lib. Each one prints one line
 * per question, so the two transcripts compare line by line. An mxArray is printed by describe():
 *
 *   class(id) [dims] n=<numel> es=<element size> is=<predicates> <data>
 *
 * with numbers in a form that does not depend on the C library (NaN, Inf, -0), characters as
 * themselves when printable ASCII and \uXXXX otherwise, and cells and structs element by element.
 * Build with /DMATLAB_DEFAULT_RELEASE=R2017b for the separate complex API (mbuild's default) or
 * /DMATLAB_MEXCMD_RELEASE=R2018a for the interleaved one.
 */
#ifndef K10COMMON_H
#define K10COMMON_H

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifndef K10_NO_RUNTIME_HEADER
#include "mclmcrrt.h"
#endif

#if MX_HAS_INTERLEAVED_COMPLEX
#define K10_MODEL "interleaved"
#else
#define K10_MODEL "separate"
#endif

static void k10_escaped(const char *s)
{
    const unsigned char *p = (const unsigned char *)s;
    if (s == NULL)
    {
        fputs("(null)", stdout);
        return;
    }

    for (; *p; p++)
    {
        if (*p == '\n')
            fputs("\\n", stdout);
        else if (*p == '\r')
            fputs("\\r", stdout);
        else if (*p == '\t')
            fputs("\\t", stdout);
        else if (*p == '\\')
            fputs("\\\\", stdout);
        else if (*p < 32 || *p > 126)
            printf("\\x%02X", *p);
        else
            putchar(*p);
    }
}

static void k10_number(double x)
{
    if (x != x)
        fputs("NaN", stdout);
    else if (x > 1.7976931348623157e308)
        fputs("Inf", stdout);
    else if (x < -1.7976931348623157e308)
        fputs("-Inf", stdout);
    else if (x == 0 && 1 / x < 0)
        fputs("-0", stdout);
    else
        printf("%.17g", x);
}

static size_t k10_base_size(mxClassID id)
{
    switch (id)
    {
    case mxDOUBLE_CLASS: case mxINT64_CLASS: case mxUINT64_CLASS: return 8;
    case mxSINGLE_CLASS: case mxINT32_CLASS: case mxUINT32_CLASS: return 4;
    case mxINT16_CLASS: case mxUINT16_CLASS: case mxCHAR_CLASS: return 2;
    case mxINT8_CLASS: case mxUINT8_CLASS: case mxLOGICAL_CLASS: return 1;
    default: return 0;
    }
}

static void k10_value(mxClassID id, const void *base, size_t at)
{
    const char *p = (const char *)base + at;
    switch (id)
    {
    case mxDOUBLE_CLASS: k10_number(*(const double *)p); break;
    case mxSINGLE_CLASS: k10_number(*(const float *)p); break;
    case mxINT8_CLASS: printf("%d", *(const signed char *)p); break;
    case mxUINT8_CLASS: printf("%u", *(const unsigned char *)p); break;
    case mxINT16_CLASS: printf("%d", *(const short *)p); break;
    case mxUINT16_CLASS: printf("%u", *(const unsigned short *)p); break;
    case mxINT32_CLASS: printf("%d", *(const int *)p); break;
    case mxUINT32_CLASS: printf("%u", *(const unsigned int *)p); break;
    case mxINT64_CLASS: printf("%lld", *(const long long *)p); break;
    case mxUINT64_CLASS: printf("%llu", *(const unsigned long long *)p); break;
    case mxLOGICAL_CLASS: printf("%d", *(const unsigned char *)p); break;
    default: fputs("?", stdout); break;
    }
}

/* count values of a numeric array, real or complex, in the client's complex model. */
static void k10_numbers(const mxArray *a, mxClassID id, size_t count)
{
    size_t size = k10_base_size(id);
    const void *re = mxGetData(a);
    size_t i;
    fputs(" {", stdout);
    if (re == NULL)
    {
        fputs("data NULL}", stdout);
        return;
    }

    for (i = 0; i < count; i++)
    {
        if (i > 0)
            fputs(" ", stdout);
        if (mxIsComplex(a))
        {
#if MX_HAS_INTERLEAVED_COMPLEX
            fputs("(", stdout);
            k10_value(id, re, 2 * i * size);
            fputs(",", stdout);
            k10_value(id, re, (2 * i + 1) * size);
            fputs(")", stdout);
#else
            const void *im = mxGetImagData(a);
            fputs("(", stdout);
            k10_value(id, re, i * size);
            fputs(",", stdout);
            if (im == NULL)
                fputs("imag NULL", stdout);
            else
                k10_value(id, im, i * size);
            fputs(")", stdout);
#endif
        }
        else
        {
            k10_value(id, re, i * size);
        }
    }

    fputs("}", stdout);
}

static void k10_describe(const mxArray *a);

static void k10_predicates(const mxArray *a)
{
    const char *sep = "";
    fputs(" is=", stdout);
#define K10_IS(test, name) if (test(a)) { printf("%s%s", sep, name); sep = ","; }
    K10_IS(mxIsNumeric, "numeric")
    K10_IS(mxIsLogical, "logical")
    K10_IS(mxIsChar, "char")
    K10_IS(mxIsCell, "cell")
    K10_IS(mxIsStruct, "struct")
    K10_IS(mxIsSparse, "sparse")
    K10_IS(mxIsComplex, "complex")
    K10_IS(mxIsEmpty, "empty")
    K10_IS(mxIsScalar, "scalar")
    K10_IS(mxIsObject, "object")
    K10_IS(mxIsOpaque, "opaque")
    K10_IS(mxIsFunctionHandle, "fhandle")
    K10_IS(mxIsDouble, "double")
    K10_IS(mxIsSingle, "single")
    K10_IS(mxIsInt8, "int8")
    K10_IS(mxIsUint8, "uint8")
    K10_IS(mxIsInt16, "int16")
    K10_IS(mxIsUint16, "uint16")
    K10_IS(mxIsInt32, "int32")
    K10_IS(mxIsUint32, "uint32")
    K10_IS(mxIsInt64, "int64")
    K10_IS(mxIsUint64, "uint64")
    K10_IS(mxIsLogicalScalar, "logicalscalar")
    K10_IS(mxIsLogicalScalarTrue, "true")
    K10_IS(mxIsFromGlobalWS, "global")
#undef K10_IS
    if (*sep == '\0')
        fputs("-", stdout);
}

static void k10_shape(const mxArray *a)
{
    mwSize nd, k;
    const mwSize *d;
    if (a == NULL)
    {
        fputs("NULL", stdout);
        return;
    }

    nd = mxGetNumberOfDimensions(a);
    d = mxGetDimensions(a);
    printf("%s(%d) [", mxGetClassName(a), (int)mxGetClassID(a));
    for (k = 0; k < nd; k++)
        printf(k ? "x%zu" : "%zu", (size_t)d[k]);
    printf("] n=%zu es=%zu", (size_t)mxGetNumberOfElements(a), (size_t)mxGetElementSize(a));
    k10_predicates(a);
}

static void k10_sparse(const mxArray *a, mxClassID id)
{
    mwIndex *ir = mxGetIr(a);
    mwIndex *jc = mxGetJc(a);
    size_t n = mxGetN(a), k, nnz;
    printf(" nzmax=%zu", (size_t)mxGetNzmax(a));
    if (ir == NULL || jc == NULL)
    {
        fputs(" ir/jc NULL", stdout);
        return;
    }

    nnz = (size_t)jc[n];
    fputs(" jc=[", stdout);
    for (k = 0; k <= n; k++)
        printf(k ? " %zu" : "%zu", (size_t)jc[k]);
    fputs("] ir=[", stdout);
    for (k = 0; k < nnz; k++)
        printf(k ? " %zu" : "%zu", (size_t)ir[k]);
    fputs("]", stdout);
    k10_numbers(a, id, nnz);
}

static void k10_describe(const mxArray *a)
{
    mxClassID id;
    size_t n, i;
    if (a == NULL)
    {
        fputs("NULL", stdout);
        return;
    }

    k10_shape(a);
    id = mxGetClassID(a);
    n = mxGetNumberOfElements(a);
    if (id == mxCELL_CLASS)
    {
        fputs(" {", stdout);
        for (i = 0; i < n; i++)
        {
            if (i > 0)
                fputs("; ", stdout);
            k10_describe(mxGetCell(a, i));
        }
        fputs("}", stdout);
    }
    else if (id == mxSTRUCT_CLASS)
    {
        int nf = mxGetNumberOfFields(a), f;
        fputs(" <", stdout);
        for (f = 0; f < nf; f++)
            printf(f ? ",%s" : "%s", mxGetFieldNameByNumber(a, f));
        fputs("> {", stdout);
        for (i = 0; i < n; i++)
        {
            for (f = 0; f < nf; f++)
            {
                if (i > 0 || f > 0)
                    fputs("; ", stdout);
                printf("%s=", mxGetFieldNameByNumber(a, f));
                k10_describe(mxGetFieldByNumber(a, i, f));
            }
        }
        fputs("}", stdout);
    }
    else if (id == mxCHAR_CLASS)
    {
        const mxChar *c = mxGetChars(a);
        fputs(" '", stdout);
        for (i = 0; c != NULL && i < n; i++)
        {
            unsigned code = (unsigned)c[i];
            if (code >= 32 && code <= 126 && code != '\'' && code != '\\')
                putchar((int)code);
            else
                printf("\\u%04X", code);
        }
        fputs(c == NULL ? "' chars NULL" : "'", stdout);
    }
    else if (k10_base_size(id) > 0)
    {
        if (mxIsSparse(a))
            k10_sparse(a, id);
        else
            k10_numbers(a, id, n);
    }
}

static void k10_line(const char *label, const mxArray *a)
{
    printf("%s: ", label);
    k10_describe(a);
    putchar('\n');
    fflush(stdout);
}

static void k10_shape_line(const char *label, const mxArray *a)
{
    printf("%s: ", label);
    k10_shape(a);
    putchar('\n');
    fflush(stdout);
}

static void k10_say(const char *label, const char *text)
{
    printf("%s: ", label);
    k10_escaped(text);
    putchar('\n');
    fflush(stdout);
}

static void k10_int(const char *label, long long value)
{
    printf("%s: %lld\n", label, value);
    fflush(stdout);
}

static void k10_double(const char *label, double value)
{
    printf("%s: ", label);
    k10_number(value);
    putchar('\n');
    fflush(stdout);
}

static int k10_out_handler(const char *s)
{
    fputs("OUT<<", stdout);
    k10_escaped(s);
    fputs(">>\n", stdout);
    fflush(stdout);
    return s == NULL ? 0 : (int)strlen(s);
}

static int k10_err_handler(const char *s)
{
    fputs("ERR<<", stdout);
    k10_escaped(s);
    fputs(">>\n", stdout);
    fflush(stdout);
    return s == NULL ? 0 : (int)strlen(s);
}

#endif
