/* A client of a Production Server's protobuf interface through MathWorks' own C client library
 * (compiler and SDK plan, stage K16): mpsclient.dll from R2025b's toolbox\compiler_sdk\mps_clients\c,
 * compiled by build-k16c.ps1 with MSVC and run against muserve and jgraph-mps alike. One line per
 * case: label<TAB>text, with the volatile parts replaced by tokens.
 *
 *     k16c.exe <base url> <component>
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include "mps/client.h"

static mpsClientRuntime* runtime;
static mpsClientContext* context;
static const char* base;
static const char* component;
static char url[1024];

/* --- printing ---------------------------------------------------------------------------------- */

static char line[1 << 20];
static size_t at;

static void put(const char* text)
{
    size_t n = strlen(text);
    if (at + n < sizeof(line) - 1) {
        memcpy(line + at, text, n);
        at += n;
        line[at] = 0;
    }
}

static void putf(const char* format, double x)
{
    char buffer[64];
    sprintf(buffer, format, x);
    put(buffer);
}

static void flush(const char* label)
{
    size_t i;
    printf("%s\t", label);
    for (i = 0; i < at; i++) {
        unsigned char c = (unsigned char)line[i];
        if (c == '(' && strncmp(line + i, "(request id=", 12) == 0) {
            /* Each server's own request numbers: the token the other clients print. */
            const char* close = strchr(line + i, ')');
            if (close != NULL) {
                printf("(request id=<N>)");
                i = (size_t)(close - line);
                continue;
            }
        }
        if (c == '\n') {
            printf("\\n");
        } else if (c == '\r') {
            printf("\\r");
        } else if (c == '\t' || (c >= 32 && c < 127)) {
            putchar(c);
        } else {
            printf("\\x%02x", c);
        }
    }
    printf("\n");
    fflush(stdout);
    at = 0;
    line[0] = 0;
}

static const char* basename_of(const char* path)
{
    const char* slash = strrchr(path, '\\');
    const char* fwd = strrchr(path, '/');
    if (fwd > slash) {
        slash = fwd;
    }
    return slash ? slash + 1 : path;
}

/* --- an mpsArray in words --------------------------------------------------------------------- */

static void show(const mpsArray* a);

static void show_number(const mpsArray* a, size_t i, int imag)
{
    const void* data = imag ? mpsGetImagData(a) : mpsIsLogical(a) ? (const void*)mpsGetLogicals(a) : mpsGetData(a);
    if (data == NULL) {
        put("<null>");
        return;
    }
    switch (mpsGetClassID(a)) {
    case mpsDOUBLE_CLASS: putf("%.17g", ((const double*)data)[i]); break;
    case mpsSINGLE_CLASS: putf("%.9g", ((const float*)data)[i]); break;
    case mpsINT8_CLASS: putf("%.17g", ((const signed char*)data)[i]); break;
    case mpsUINT8_CLASS: putf("%.17g", ((const unsigned char*)data)[i]); break;
    case mpsINT16_CLASS: putf("%.17g", ((const short*)data)[i]); break;
    case mpsUINT16_CLASS: putf("%.17g", ((const unsigned short*)data)[i]); break;
    case mpsINT32_CLASS: putf("%.17g", ((const int*)data)[i]); break;
    case mpsUINT32_CLASS: putf("%.17g", ((const unsigned int*)data)[i]); break;
    case mpsINT64_CLASS: { char b[32]; sprintf(b, "%lld", ((const long long*)data)[i]); put(b); break; }
    case mpsUINT64_CLASS: { char b[32]; sprintf(b, "%llu", ((const unsigned long long*)data)[i]); put(b); break; }
    case mpsLOGICAL_CLASS: put(((const mpsLogical*)data)[i] ? "1" : "0"); break;
    default: put("?"); break;
    }
}

static void show(const mpsArray* a)
{
    mpsSize nd, d, n, i;
    const mpsSize* dims;
    char buffer[64];
    if (a == NULL) {
        put("<null array>");
        return;
    }
    put(mpsGetClassName(a));
    nd = mpsGetNumberOfDimensions(a);
    dims = mpsGetDimensions(a);
    put(" [");
    for (d = 0; d < nd; d++) {
        sprintf(buffer, "%s%llu", d ? " " : "", (unsigned long long)dims[d]);
        put(buffer);
    }
    put("]");
    if (mpsIsSparse(a)) {
        put(" sparse");
    }
    if (mpsIsComplex(a)) {
        put(" complex");
    }
    n = (mpsSize)mpsGetNumberOfElements(a);
    if (mpsIsSparse(a)) {
        const mpsIndex* ir = mpsGetIr(a);
        const mpsIndex* jc = mpsGetJc(a);
        mpsSize cols = dims[1], c, k;
        put(" {");
        for (c = 0; c < cols; c++) {
            for (k = jc[c]; k < jc[c + 1]; k++) {
                sprintf(buffer, "%s(%llu,%llu)=", (c || k != jc[0]) ? " " : "", (unsigned long long)ir[k], (unsigned long long)c);
                put(buffer);
                show_number(a, (size_t)k, 0);
            }
        }
        put("}");
        return;
    }
    if (mpsIsChar(a)) {
        const mpsChar* chars = mpsGetChars(a);
        put(" '");
        for (i = 0; i < n; i++) {
            unsigned int c = (unsigned int)chars[i];
            if (c >= 32 && c < 127) {
                buffer[0] = (char)c;
                buffer[1] = 0;
            } else {
                sprintf(buffer, "\\u%04x", c);
            }
            put(buffer);
        }
        put("'");
        return;
    }
    if (mpsIsCell(a)) {
        put(" {");
        for (i = 0; i < n; i++) {
            if (i) {
                put("; ");
            }
            show(mpsGetCell(a, i));
        }
        put("}");
        return;
    }
    if (mpsIsStruct(a)) {
        int nf = mpsGetNumberOfFields(a), f;
        put(" fields [");
        for (f = 0; f < nf; f++) {
            if (f) {
                put(",");
            }
            put(mpsGetFieldNameByNumber(a, f));
        }
        put("]");
        for (i = 0; i < n; i++) {
            put(" (");
            for (f = 0; f < nf; f++) {
                if (f) {
                    put(", ");
                }
                put(mpsGetFieldNameByNumber(a, f));
                put("=");
                show(mpsGetFieldByNumber(a, i, f));
            }
            put(")");
        }
        return;
    }
    if (mpsIsNumeric(a) || mpsIsLogical(a)) {
        put(" [");
        for (i = 0; i < n; i++) {
            if (i) {
                put(" ");
            }
            show_number(a, (size_t)i, 0);
            if (mpsIsComplex(a)) {
                put("+");
                show_number(a, (size_t)i, 1);
                put("i");
            }
        }
        put("]");
        return;
    }
    put(" <other>");
}

/* --- calls -------------------------------------------------------------------------------------- */

static void show_error(void)
{
    mpsErrorInfo err;
    char buffer[64];
    size_t i;
    runtime->getLastErrorInfo(context, &err);
    switch (err.type) {
    case MPS_HTTP_ERROR_INFO:
        sprintf(buffer, "HTTP %u ", err.details.http.responseCode);
        put(buffer);
        put(err.details.http.responseMessage ? err.details.http.responseMessage : "");
        break;
    case MPS_MATLAB_ERROR_INFO:
        put("MATLAB id=");
        put(err.details.matlab.identifier ? err.details.matlab.identifier : "");
        put(" message=");
        put(err.details.matlab.message ? err.details.matlab.message : "");
        put(" stack=[");
        for (i = 0; i < err.details.matlab.matlabStackDepth; i++) {
            if (i) {
                put(";");
            }
            put(err.details.matlab.matlabStack[i].function);
            put("@<F>/");
            put(basename_of(err.details.matlab.matlabStack[i].file));
            sprintf(buffer, ":%d", err.details.matlab.matlabStack[i].line);
            put(buffer);
        }
        put("]");
        break;
    default:
        put("GENERIC ");
        put(err.details.general.genericErrorMsg ? err.details.general.genericErrorMsg : "");
        break;
    }
    put(" | ");
    put(err.message ? err.message : "");
    runtime->destroyLastErrorInfo(&err);
}

/* Calls function with the inputs, nlhs outputs; prints the outputs or the error; frees the inputs. */
static void call(const char* label, const char* function, int nlhs, int nrhs, mpsArray** prhs, const char* comp)
{
    mpsArray* plhs[8] = { 0 };
    mpsStatus status;
    int i;
    sprintf(url, "%s/%s/%s", base, comp ? comp : component, function);
    status = runtime->feval(context, url, nlhs, plhs, nrhs, (const mpsArray**)prhs);
    if (status == MPS_OK) {
        char buffer[32];
        sprintf(buffer, "%d outputs [", nlhs);
        put(buffer);
        for (i = 0; i < nlhs; i++) {
            if (i) {
                put("; ");
            }
            show(plhs[i]);
            if (plhs[i]) {
                mpsDestroyArray(plhs[i]);
            }
        }
        put("]");
    } else {
        show_error();
    }
    for (i = 0; i < nrhs; i++) {
        mpsDestroyArray(prhs[i]);
    }
    flush(label);
}

static mpsArray* scalar(double x)
{
    return mpsCreateDoubleScalar(x);
}

static mpsArray* matrix(mpsSize m, mpsSize n, const double* columnMajor)
{
    mpsArray* a = mpsCreateDoubleMatrix(m, n, mpsREAL);
    if (columnMajor) {
        memcpy(mpsGetPr(a), columnMajor, (size_t)(m * n) * sizeof(double));
    }
    return a;
}

static mpsArray* numeric(mpsSize m, mpsSize n, mpsClassID cls, const void* columnMajor, size_t elementSize)
{
    mpsArray* a = mpsCreateNumericMatrix(m, n, cls, mpsREAL);
    memcpy(mpsGetData(a), columnMajor, (size_t)(m * n) * elementSize);
    return a;
}

static void describe(const char* label, mpsArray* value)
{
    mpsArray* prhs[1];
    prhs[0] = value;
    call(label, "k14describe", 1, 1, prhs, NULL);
}

static void make(const char* label, const char* kind)
{
    mpsArray* prhs[1];
    prhs[0] = mpsCreateString(kind);
    call(label, "k14make", 1, 1, prhs, NULL);
}

int main(int argc, char** argv)
{
    mpsClientConfig* config;
    mpsArray* prhs[4];
    static const char* kinds[] = {
        "double", "negzero", "row", "column", "matrix", "nd", "empty", "empty03", "single", "int8", "int8s", "uint8", "int32", "int64", "uint64",
        "complex", "complexrow", "complexsingle", "logical", "logicals", "char", "charmatrix", "emptychar", "unicode", "quote", "controls",
        "string", "strings", "stringmissing", "cell", "cellcol", "nestedcell", "emptycell", "struct", "structnested", "structarray",
        "emptystruct", "nofields", "sparse", "sparselogical", "inf", "nan", "pi", "big", "enum", "enums", "handle", "datetime", "datetimeutc",
        "table", "map", "categorical", "duration", "dict", "large", "nosuchkind", NULL
    };
    static const char* errors[] = { "plain", "id", "nested", "cause", "builtin", "undefined", "multiline", "quote", "none", NULL };
    const char** k;
    int nIn, nOut;
    if (argc < 3) {
        fprintf(stderr, "k16c.exe <base url> <component>\n");
        return 2;
    }
    base = argv[1];
    component = argv[2];
    runtime = mpsInitialize();
    runtime->createConfig(&config);
    if (runtime->createContext(&context, config) != MPS_OK) {
        fprintf(stderr, "no client context\n");
        return 1;
    }

    prhs[0] = scalar(1);
    prhs[1] = scalar(2);
    call("basic", "k14add", 1, 2, prhs, NULL);
    /* Values in, as the library marshals them, described by the function. */
    describe("in.double", scalar(2.5));
    {
        static const double m[] = { 1, 4, 2, 5, 3, 6 };
        describe("in.matrix", matrix(2, 3, m));
    }
    {
        static const double v[] = { 1, 2, 3 };
        describe("in.row", matrix(1, 3, v));
        describe("in.column", matrix(3, 1, v));
    }
    describe("in.empty", matrix(0, 0, NULL));
    describe("in.empty03", matrix(0, 3, NULL));
    {
        mpsSize dims[3] = { 2, 2, 2 };
        static const double v[] = { 1, 5, 3, 7, 2, 6, 4, 8 };
        mpsArray* a = mpsCreateNumericArray(3, dims, mpsDOUBLE_CLASS, mpsREAL);
        memcpy(mpsGetPr(a), v, sizeof(v));
        describe("in.nd", a);
    }
    {
        mpsArray* a = mpsCreateDoubleMatrix(1, 2, mpsCOMPLEX);
        mpsGetPr(a)[0] = 1; mpsGetPr(a)[1] = 3;
        mpsGetPi(a)[0] = 2; mpsGetPi(a)[1] = -4;
        describe("in.complex", a);
    }
    {
        mpsArray* a = mpsCreateNumericMatrix(1, 1, mpsSINGLE_CLASS, mpsCOMPLEX);
        ((float*)mpsGetData(a))[0] = 2.0f;
        ((float*)mpsGetImagData(a))[0] = 3.0f;
        describe("in.complex.single", a);
    }
    {
        mpsArray* a = mpsCreateNumericMatrix(1, 1, mpsINT8_CLASS, mpsCOMPLEX);
        ((signed char*)mpsGetData(a))[0] = 1;
        ((signed char*)mpsGetImagData(a))[0] = 2;
        describe("in.complex.int8", a);
    }
    {
        static const double specials[] = { 0, 0, 0, -0.0 };
        mpsArray* a = matrix(1, 4, specials);
        mpsGetPr(a)[0] = NAN; mpsGetPr(a)[1] = INFINITY; mpsGetPr(a)[2] = -INFINITY;
        describe("in.specials", a);
    }
    { float v[] = { 1.5f }; describe("in.single", numeric(1, 1, mpsSINGLE_CLASS, v, sizeof(float))); }
    { signed char v[] = { -128, 0, 127 }; describe("in.int8", numeric(1, 3, mpsINT8_CLASS, v, 1)); }
    { unsigned char v[] = { 0, 128, 255 }; describe("in.uint8", numeric(1, 3, mpsUINT8_CLASS, v, 1)); }
    { short v[] = { -32768, 7, 32767 }; describe("in.int16", numeric(1, 3, mpsINT16_CLASS, v, 2)); }
    { unsigned short v[] = { 0, 65535 }; describe("in.uint16", numeric(1, 2, mpsUINT16_CLASS, v, 2)); }
    { int v[] = { -2147483647 - 1, -100000, 2147483647 }; describe("in.int32", numeric(1, 3, mpsINT32_CLASS, v, 4)); }
    { unsigned int v[] = { 0, 4294967295u }; describe("in.uint32", numeric(1, 2, mpsUINT32_CLASS, v, 4)); }
    { long long v[] = { -9007199254740993LL, 9223372036854775807LL }; describe("in.int64", numeric(1, 2, mpsINT64_CLASS, v, 8)); }
    { unsigned long long v[] = { 9007199254740993ULL, 18446744073709551615ULL }; describe("in.uint64", numeric(1, 2, mpsUINT64_CLASS, v, 8)); }
    describe("in.logical", mpsCreateLogicalScalar(1));
    {
        mpsArray* a = mpsCreateLogicalMatrix(2, 2);
        mpsGetLogicals(a)[0] = 1; mpsGetLogicals(a)[3] = 1;
        describe("in.logicals", a);
    }
    describe("in.char", mpsCreateString("hello"));
    describe("in.empty.char", mpsCreateString(""));
    describe("in.utf8", mpsCreateString("\xce\xbb\xe2\x82\xac" "A"));
    {
        const char* rows[] = { "abc", "def" };
        describe("in.charmatrix", mpsCreateCharMatrixFromStrings(2, rows));
    }
    {
        const char* rows[] = { "ab", "c" };
        describe("in.charmatrix.ragged", mpsCreateCharMatrixFromStrings(2, rows));
    }
    {
        mpsSize dims[2] = { 1, 3 };
        mpsArray* a = mpsCreateCharArray(2, dims);
        mpsGetChars(a)[0] = 0x3bb; mpsGetChars(a)[1] = 0x20ac; mpsGetChars(a)[2] = 'A';
        describe("in.chars", a);
    }
    {
        mpsSize dims[2] = { 1, 2 };
        mpsArray* a = mpsCreateCharArray(2, dims);
        mpsGetChars(a)[0] = 0xd83d; mpsGetChars(a)[1] = 0xde00;
        describe("in.surrogates", a);
    }
    {
        mpsArray* a = mpsCreateCellMatrix(1, 3);
        static const double m[] = { 3, 5, 4, 6 };
        mpsSetCell(a, 0, scalar(1));
        mpsSetCell(a, 1, mpsCreateString("two"));
        mpsSetCell(a, 2, matrix(2, 2, m));
        describe("in.cell", a);
    }
    describe("in.cell.empty", mpsCreateCellMatrix(0, 0));
    describe("in.cell.holes", mpsCreateCellMatrix(1, 2));
    {
        mpsArray* a = mpsCreateCellMatrix(1, 2);
        mpsArray* inner = mpsCreateCellMatrix(1, 1);
        mpsSetCell(inner, 0, scalar(1));
        mpsSetCell(a, 0, inner);
        mpsSetCell(a, 1, mpsCreateCellMatrix(0, 0));
        describe("in.cell.nested", a);
    }
    {
        const char* names[] = { "name", "value" };
        static const double v[] = { 1, 2, 3 };
        mpsArray* a = mpsCreateStructMatrix(1, 1, 2, names);
        mpsSetField(a, 0, "name", mpsCreateString("probe"));
        mpsSetField(a, 0, "value", matrix(1, 3, v));
        describe("in.struct", a);
    }
    {
        const char* names[] = { "a" };
        mpsArray* a = mpsCreateStructMatrix(1, 3, 1, names);
        mpsSetField(a, 0, "a", scalar(1));
        mpsSetField(a, 1, "a", scalar(2));
        mpsSetField(a, 2, "a", scalar(3));
        describe("in.struct.array", a);
    }
    {
        const char* names[] = { "a", "b" };
        mpsArray* a = mpsCreateStructMatrix(1, 1, 2, names);
        mpsSetField(a, 0, "a", scalar(1));
        describe("in.struct.hole", a);
    }
    describe("in.struct.nofields", mpsCreateStructMatrix(1, 1, 0, NULL));
    {
        const char* names[] = { "a" };
        describe("in.struct.empty", mpsCreateStructMatrix(0, 0, 1, names));
    }
    {
        /* sparse([1 3], [1 2], [5 6], 3, 3): ir [0 2], jc [0 1 2 2], pr [5 6]. */
        mpsArray* a = mpsCreateSparse(3, 3, 2, mpsREAL);
        mpsGetIr(a)[0] = 0; mpsGetIr(a)[1] = 2;
        mpsGetJc(a)[0] = 0; mpsGetJc(a)[1] = 1; mpsGetJc(a)[2] = 2; mpsGetJc(a)[3] = 2;
        mpsGetPr(a)[0] = 5; mpsGetPr(a)[1] = 6;
        describe("in.sparse", a);
    }
    {
        mpsArray* a = mpsCreateSparseLogicalMatrix(2, 2, 2);
        mpsGetIr(a)[0] = 0; mpsGetIr(a)[1] = 1;
        mpsGetJc(a)[0] = 0; mpsGetJc(a)[1] = 1; mpsGetJc(a)[2] = 2;
        mpsGetLogicals(a)[0] = 1; mpsGetLogicals(a)[1] = 1;
        describe("in.sparse.logical", a);
    }
    {
        mpsArray* a = mpsCreateSparse(2, 2, 1, mpsCOMPLEX);
        mpsGetIr(a)[0] = 0;
        mpsGetJc(a)[0] = 0; mpsGetJc(a)[1] = 1; mpsGetJc(a)[2] = 1;
        mpsGetPr(a)[0] = 1; mpsGetPi(a)[0] = 2;
        describe("in.sparse.complex", a);
    }
    {
        mpsArray* a = matrix(1, 100000, NULL);
        mpsSize i;
        for (i = 0; i < 100000; i++) {
            mpsGetPr(a)[i] = (double)i;
        }
        prhs[0] = a;
        call("large.in", "k14nargs", 1, 1, prhs, NULL);
    }
    /* Values out. */
    for (k = kinds; *k; k++) {
        char label[64];
        sprintf(label, "out.%s", *k);
        make(label, *k);
    }
    /* Errors. */
    for (k = errors; *k; k++) {
        char label[64];
        sprintf(label, "err.%s", *k);
        prhs[0] = mpsCreateString(*k);
        call(label, "k14err", 1, 1, prhs, NULL);
    }
    prhs[0] = scalar(1); prhs[1] = scalar(2); prhs[2] = scalar(3);
    call("err.too.many.in", "k14add", 1, 3, prhs, NULL);
    prhs[0] = scalar(1);
    call("err.too.few.in", "k14add", 1, 1, prhs, NULL);
    /* Output counts. */
    prhs[0] = scalar(2);
    call("multi.3", "k14multi", 3, 1, prhs, NULL);
    prhs[0] = scalar(2);
    call("multi.1", "k14multi", 1, 1, prhs, NULL);
    prhs[0] = scalar(2);
    call("multi.0", "k14multi", 0, 1, prhs, NULL);
    prhs[0] = scalar(2);
    call("multi.4", "k14multi", 4, 1, prhs, NULL);
    prhs[0] = scalar(1); prhs[1] = mpsCreateString("a"); prhs[2] = mpsCreateLogicalScalar(1);
    call("echo.3.3", "k14echo", 3, 3, prhs, NULL);
    call("echo.0.1", "k14echo", 1, 0, prhs, NULL);
    prhs[0] = scalar(1); prhs[1] = scalar(2);
    call("nargout.2.add", "k14add", 2, 2, prhs, NULL);
    prhs[0] = scalar(5);
    call("noout.0", "k14noout", 0, 1, prhs, NULL);
    prhs[0] = scalar(5);
    call("noout.1", "k14noout", 1, 1, prhs, NULL);
    prhs[0] = scalar(2);
    call("print", "k14print", 1, 1, prhs, NULL);
    for (nIn = 0; nIn <= 2; nIn++) {
        for (nOut = 0; nOut <= 3; nOut++) {
            char label[32];
            int i;
            sprintf(label, "nargs.%d.%d", nIn, nOut);
            for (i = 0; i < nIn; i++) {
                prhs[i] = scalar(i + 1);
            }
            call(label, "k14nargs", nOut, nIn, prhs, NULL);
        }
    }
    call("state.0", "k14state", 1, 0, prhs, NULL);
    call("state.1", "k14state", 1, 0, prhs, NULL);
    prhs[0] = scalar(1);
    call("missing.function", "nosuch", 1, 1, prhs, NULL);
    prhs[0] = scalar(1); prhs[1] = scalar(2);
    call("missing.component", "k14add", 1, 2, prhs, "nosuch");

    runtime->destroyConfig(config);
    runtime->destroyContext(context);
    mpsTerminate();
    return 0;
}
