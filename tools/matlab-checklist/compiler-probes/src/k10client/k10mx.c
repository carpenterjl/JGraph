/*
 * k10mx: the mxArray C API with no component (compiler and SDK plan, stage K10).
 *
 *   k10mx            every safe question, in order
 *   k10mx risky N    risky question N alone (a misuse R2025b may refuse, ignore or crash on);
 *                    record-k10.ps1 runs each in a process of its own
 *
 * Built once per complex model (see k10common.h); the model is the first line printed.
 */
#include "k10common.h"

static const char *Names2[] = { "a", "b" };

static mxArray *Row(double first, size_t n)
{
    size_t i;
    mxArray *a = mxCreateDoubleMatrix(1, n, mxREAL);
    double *p = (double *)mxGetData(a);
    for (i = 0; i < n; i++)
        p[i] = first + (double)i;
    return a;
}

static void Create(void)
{
    mxClassID id;
    mxArray *a;
    char label[64];
    static const mwSize d234[] = { 2, 3, 4 }, d2311[] = { 2, 3, 1, 1 }, d213[] = { 2, 1, 3 }, d031[] = { 0, 3, 1 };
    static const mwSize d4[] = { 4 }, d502[] = { 5, 0, 2 }, d111[] = { 1, 1, 1 }, d122[] = { 1, 2, 2 }, d23[] = { 2, 3 };
    static const mwSize d1[] = { 1 }, d0[] = { 0 };

    a = mxCreateDoubleMatrix(2, 3, mxREAL); k10_line("create.double2x3", a); mxDestroyArray(a);
    a = mxCreateDoubleMatrix(0, 0, mxREAL); k10_line("create.double0x0", a); mxDestroyArray(a);
    a = mxCreateDoubleMatrix(0, 3, mxREAL); k10_line("create.double0x3", a); mxDestroyArray(a);
    a = mxCreateDoubleMatrix(3, 0, mxREAL); k10_line("create.double3x0", a); mxDestroyArray(a);
    a = mxCreateDoubleScalar(2.5); k10_line("create.scalar", a); mxDestroyArray(a);
    a = mxCreateDoubleScalar(-0.0); k10_line("create.negzero", a); mxDestroyArray(a);
    a = mxCreateDoubleMatrix(2, 2, mxCOMPLEX); k10_line("create.complex2x2", a); mxDestroyArray(a);
    for (id = mxDOUBLE_CLASS; id <= mxUINT64_CLASS; id = (mxClassID)(id + 1))
    {
        sprintf(label, "create.numeric.%d", (int)id);
        a = mxCreateNumericMatrix(1, 2, id, mxREAL); k10_line(label, a); mxDestroyArray(a);
        sprintf(label, "create.numericz.%d", (int)id);
        a = mxCreateNumericMatrix(1, 2, id, mxCOMPLEX); k10_line(label, a); mxDestroyArray(a);
    }

    a = mxCreateNumericArray(3, d234, mxDOUBLE_CLASS, mxREAL); k10_shape_line("create.nd234", a); mxDestroyArray(a);
    a = mxCreateNumericArray(4, d2311, mxDOUBLE_CLASS, mxREAL); k10_shape_line("create.nd2311", a); mxDestroyArray(a);
    a = mxCreateNumericArray(3, d213, mxINT8_CLASS, mxREAL); k10_shape_line("create.nd213", a); mxDestroyArray(a);
    a = mxCreateNumericArray(3, d031, mxDOUBLE_CLASS, mxREAL); k10_shape_line("create.nd031", a); mxDestroyArray(a);
    a = mxCreateNumericArray(1, d4, mxDOUBLE_CLASS, mxREAL); k10_shape_line("create.nd1", a); mxDestroyArray(a);
    a = mxCreateNumericArray(0, d4, mxDOUBLE_CLASS, mxREAL); k10_shape_line("create.nd0", a); mxDestroyArray(a);
    a = mxCreateNumericArray(3, d502, mxUINT16_CLASS, mxREAL); k10_shape_line("create.nd502", a); mxDestroyArray(a);
    a = mxCreateNumericArray(3, d111, mxDOUBLE_CLASS, mxREAL); k10_shape_line("create.nd111", a); mxDestroyArray(a);
    a = mxCreateNumericArray(3, d122, mxSINGLE_CLASS, mxCOMPLEX); k10_line("create.nd122z", a); mxDestroyArray(a);
    a = mxCreateUninitNumericMatrix(2, 2, mxDOUBLE_CLASS, mxREAL); k10_shape_line("create.uninit2x2", a); mxDestroyArray(a);
    {
        size_t dims[] = { 2, 2, 2 };
        a = mxCreateUninitNumericArray(3, dims, mxINT8_CLASS, mxREAL); k10_shape_line("create.uninit222", a); mxDestroyArray(a);
    }

    a = mxCreateLogicalScalar(true); k10_line("create.true", a); mxDestroyArray(a);
    a = mxCreateLogicalScalar(false); k10_line("create.false", a); mxDestroyArray(a);
    a = mxCreateLogicalMatrix(2, 2); k10_line("create.logical2x2", a); mxDestroyArray(a);
    a = mxCreateLogicalArray(3, d122); k10_line("create.logical122", a); mxDestroyArray(a);

    a = mxCreateString("hello"); k10_line("create.string", a); mxDestroyArray(a);
    a = mxCreateString(""); k10_line("create.emptystring", a); mxDestroyArray(a);
    a = mxCreateStringFromNChars("abcdef", 3); k10_line("create.nchars", a); mxDestroyArray(a);
    a = mxCreateString("caf\xC3\xA9"); k10_line("create.utf8", a); mxDestroyArray(a);
    a = mxCreateString("caf\xE9"); k10_line("create.latin1", a); mxDestroyArray(a);
    {
        const char *strs[] = { "a", "bcd", "ef" };
        a = mxCreateCharMatrixFromStrings(3, strs); k10_line("create.charmatrix", a); mxDestroyArray(a);
        a = mxCreateCharMatrixFromStrings(1, strs); k10_line("create.charmatrix1", a); mxDestroyArray(a);
        a = mxCreateCharMatrixFromStrings(0, strs); k10_line("create.charmatrix0", a); mxDestroyArray(a);
    }
    a = mxCreateCharArray(2, d23); k10_line("create.chararray23", a); mxDestroyArray(a);
    a = mxCreateCharArray(3, d122); k10_line("create.chararray122", a); mxDestroyArray(a);
    a = mxCreateCharArray(1, d1); k10_line("create.chararray1", a); mxDestroyArray(a);

    a = mxCreateCellMatrix(2, 2); k10_line("create.cell2x2", a); mxDestroyArray(a);
    a = mxCreateCellMatrix(0, 0); k10_line("create.cell0x0", a); mxDestroyArray(a);
    a = mxCreateCellArray(3, d122); k10_line("create.cell122", a); mxDestroyArray(a);
    a = mxCreateCellArray(1, d0); k10_line("create.cellnd0", a); mxDestroyArray(a);

    a = mxCreateStructMatrix(1, 2, 2, Names2); k10_line("create.struct1x2", a); mxDestroyArray(a);
    a = mxCreateStructMatrix(1, 1, 0, NULL); k10_line("create.struct0fields", a); mxDestroyArray(a);
    a = mxCreateStructMatrix(0, 0, 2, Names2); k10_line("create.struct0x0", a); mxDestroyArray(a);
    {
        const char *x[] = { "x" };
        a = mxCreateStructArray(2, d23, 1, x); k10_line("create.structarray23", a); mxDestroyArray(a);
    }

    a = mxCreateSparse(3, 3, 4, mxREAL); k10_line("create.sparse", a); mxDestroyArray(a);
    a = mxCreateSparse(2, 2, 0, mxREAL); k10_line("create.sparse0", a); mxDestroyArray(a);
    a = mxCreateSparse(2, 2, 2, mxCOMPLEX); k10_line("create.sparsez", a); mxDestroyArray(a);
    a = mxCreateSparseLogicalMatrix(2, 3, 2); k10_line("create.sparselogical", a); mxDestroyArray(a);
}

static void Queries(void)
{
    static const mwSize d234[] = { 2, 3, 4 };
    mxArray *a = mxCreateNumericArray(3, d234, mxDOUBLE_CLASS, mxREAL);
    mxArray *b;
    mwIndex subs[] = { 1, 2, 3, 0 };
    k10_int("query.M", (long long)mxGetM(a));
    k10_int("query.N", (long long)mxGetN(a));
    k10_int("query.numel", (long long)mxGetNumberOfElements(a));
    k10_int("query.ndims", (long long)mxGetNumberOfDimensions(a));
    k10_int("query.sub3", (long long)mxCalcSingleSubscript(a, 3, subs));
    k10_int("query.sub2", (long long)mxCalcSingleSubscript(a, 2, subs));
    k10_int("query.sub1", (long long)mxCalcSingleSubscript(a, 1, subs + 2));
    k10_int("query.sub4", (long long)mxCalcSingleSubscript(a, 4, subs));
    mxDestroyArray(a);

    a = mxCreateNumericMatrix(1, 1, mxINT8_CLASS, mxREAL); *(signed char *)mxGetData(a) = -3;
    k10_double("scalar.int8", mxGetScalar(a)); mxDestroyArray(a);
    a = mxCreateString("A"); k10_double("scalar.char", mxGetScalar(a)); mxDestroyArray(a);
    a = mxCreateLogicalScalar(true); k10_double("scalar.logical", mxGetScalar(a)); mxDestroyArray(a);
    a = mxCreateDoubleMatrix(0, 0, mxREAL); k10_double("scalar.empty", mxGetScalar(a)); mxDestroyArray(a);
    a = mxCreateNumericMatrix(1, 1, mxUINT64_CLASS, mxREAL); *(unsigned long long *)mxGetData(a) = 18446744073709551615ULL;
    k10_double("scalar.uint64max", mxGetScalar(a)); mxDestroyArray(a);
    a = mxCreateNumericMatrix(1, 1, mxSINGLE_CLASS, mxREAL); *(float *)mxGetData(a) = 1.5f;
    k10_double("scalar.single", mxGetScalar(a)); mxDestroyArray(a);
    a = Row(4, 3); k10_double("scalar.row", mxGetScalar(a)); mxDestroyArray(a);
    a = mxCreateDoubleMatrix(1, 1, mxCOMPLEX);
    ((double *)mxGetData(a))[0] = 7;
    k10_double("scalar.complex", mxGetScalar(a)); mxDestroyArray(a);
    a = mxCreateSparse(2, 2, 1, mxREAL);
    mxGetJc(a)[0] = 0; mxGetJc(a)[1] = 0; mxGetJc(a)[2] = 1; mxGetIr(a)[0] = 1; ((double *)mxGetData(a))[0] = 9;
    k10_double("scalar.sparse", mxGetScalar(a)); mxDestroyArray(a);
    a = mxCreateCellMatrix(1, 1); k10_double("scalar.cell", mxGetScalar(a)); mxDestroyArray(a);
    a = mxCreateStructMatrix(1, 1, 2, Names2); k10_double("scalar.struct", mxGetScalar(a)); mxDestroyArray(a);

    a = mxCreateDoubleScalar(1);
    k10_int("isclass.double", mxIsClass(a, "double"));
    k10_int("isclass.Double", mxIsClass(a, "Double"));
    k10_int("isclass.numeric", mxIsClass(a, "numeric"));
    k10_int("isclass.float", mxIsClass(a, "float"));
    k10_int("isclass.single", mxIsClass(a, "single"));
    mxDestroyArray(a);
    a = mxCreateNumericMatrix(1, 1, mxINT8_CLASS, mxREAL);
    k10_int("isclass.int8.integer", mxIsClass(a, "integer"));
    k10_int("isclass.int8.int8", mxIsClass(a, "int8"));
    mxDestroyArray(a);
    a = mxCreateCellMatrix(1, 1); k10_int("isclass.cell", mxIsClass(a, "cell")); mxDestroyArray(a);
    a = mxCreateStructMatrix(1, 1, 0, NULL); k10_int("isclass.struct", mxIsClass(a, "struct")); mxDestroyArray(a);
    a = mxCreateString("x"); k10_int("isclass.char", mxIsClass(a, "char")); mxDestroyArray(a);
    a = mxCreateLogicalScalar(false); k10_int("isclass.logical", mxIsClass(a, "logical")); mxDestroyArray(a);
    a = mxCreateSparse(1, 1, 1, mxREAL); k10_int("isclass.sparse.double", mxIsClass(a, "double")); mxDestroyArray(a);

    k10_double("const.nan", mxGetNaN());
    k10_double("const.inf", mxGetInf());
    k10_double("const.eps", mxGetEps());
    k10_int("const.isnan", mxIsNaN(mxGetNaN()));
    k10_int("const.isinf", mxIsInf(-mxGetInf()));
    k10_int("const.isfinite.big", mxIsFinite(1e308));
    k10_int("const.isfinite.inf", mxIsFinite(mxGetInf()));
    k10_int("const.isfinite.nan", mxIsFinite(mxGetNaN()));
    k10_int("const.isnan.one", mxIsNaN(1));

    a = mxCreateCellMatrix(1, 1); k10_shape_line("es.cell", a); mxDestroyArray(a);
    a = mxCreateStructMatrix(1, 1, 1, Names2); k10_shape_line("es.struct", a); mxDestroyArray(a);
    a = mxCreateSparse(2, 2, 2, mxREAL); k10_shape_line("es.sparse", a); mxDestroyArray(a);
    a = mxCreateSparseLogicalMatrix(2, 2, 2); k10_shape_line("es.sparselogical", a); mxDestroyArray(a);
    b = mxCreateNumericMatrix(1, 1, mxINT32_CLASS, mxCOMPLEX); k10_shape_line("es.int32z", b); mxDestroyArray(b);
    a = mxCreateString("x"); k10_shape_line("es.char", a); mxDestroyArray(a);
}

static void Strings(void)
{
    char buf[16];
    char *s;
    mxArray *a = mxCreateString("hello");
    mxArray *b;
    const char *strs[] = { "a", "bcd", "ef" };

    s = mxArrayToString(a); k10_say("string.tostring", s); mxFree(s);
    s = mxArrayToUTF8String(a); k10_say("string.toutf8", s); mxFree(s);
    memset(buf, 'Z', sizeof(buf)); buf[15] = 0;
    k10_int("string.get3.result", mxGetString(a, buf, 3)); k10_say("string.get3", buf);
    memset(buf, 'Z', sizeof(buf)); buf[15] = 0;
    k10_int("string.get5.result", mxGetString(a, buf, 5)); k10_say("string.get5", buf);
    memset(buf, 'Z', sizeof(buf)); buf[15] = 0;
    k10_int("string.get6.result", mxGetString(a, buf, 6)); k10_say("string.get6", buf);
    memset(buf, 'Z', sizeof(buf)); buf[15] = 0;
    k10_int("string.get1.result", mxGetString(a, buf, 1)); k10_say("string.get1", buf);
    memset(buf, 'Z', sizeof(buf)); buf[15] = 0;
    mxGetNChars(a, buf, 3); k10_say("string.nchars3", buf);
    memset(buf, 'Z', sizeof(buf)); buf[15] = 0;
    mxGetNChars(a, buf, 8); k10_say("string.nchars8", buf);
    mxDestroyArray(a);

    a = mxCreateCharMatrixFromStrings(3, strs);
    s = mxArrayToString(a); k10_say("string.matrix.tostring", s); mxFree(s);
    memset(buf, 'Z', sizeof(buf)); buf[15] = 0;
    k10_int("string.matrix.get.result", mxGetString(a, buf, 16)); k10_say("string.matrix.get", buf);
    mxDestroyArray(a);

    a = mxCreateString("caf\xC3\xA9");
    s = mxArrayToString(a); k10_say("string.utf8.tostring", s); mxFree(s);
    s = mxArrayToUTF8String(a); k10_say("string.utf8.toutf8", s); mxFree(s);
    memset(buf, 'Z', sizeof(buf)); buf[15] = 0;
    k10_int("string.utf8.get.result", mxGetString(a, buf, 16)); k10_say("string.utf8.get", buf);
    mxDestroyArray(a);

    {
        static const mwSize d15[] = { 1, 5 };
        mxChar *c;
        a = mxCreateCharArray(2, d15);
        c = mxGetChars(a);
        c[0] = 'c'; c[1] = 0xE9; c[2] = 0x20AC; c[3] = 0xD83D; c[4] = 0xDE00;
        k10_line("string.wide", a);
        s = mxArrayToString(a); k10_say("string.wide.tostring", s); mxFree(s);
        s = mxArrayToUTF8String(a); k10_say("string.wide.toutf8", s); mxFree(s);
        memset(buf, 'Z', sizeof(buf)); buf[15] = 0;
        k10_int("string.wide.get.result", mxGetString(a, buf, 16)); k10_say("string.wide.get", buf);
        mxDestroyArray(a);
    }

    b = mxCreateDoubleScalar(1);
    s = mxArrayToString(b); k10_say("string.double.tostring", s); if (s) mxFree(s);
    s = mxArrayToUTF8String(b); k10_say("string.double.toutf8", s); if (s) mxFree(s);
    memset(buf, 'Z', sizeof(buf)); buf[15] = 0;
    k10_int("string.double.get.result", mxGetString(b, buf, 16)); k10_say("string.double.get", buf);
    mxDestroyArray(b);

    a = mxCreateString("");
    s = mxArrayToString(a); k10_say("string.empty.tostring", s); mxFree(s);
    memset(buf, 'Z', sizeof(buf)); buf[15] = 0;
    k10_int("string.empty.get.result", mxGetString(a, buf, 16)); k10_say("string.empty.get", buf);
    mxDestroyArray(a);

    {
        char longText[301];
        memset(longText, 'q', 300); longText[300] = 0;
        a = mxCreateString(longText);
        k10_int("string.long.n", (long long)mxGetN(a));
        mxDestroyArray(a);
    }
}

static void Structs(void)
{
    mxArray *s = mxCreateStructMatrix(1, 2, 2, Names2);
    mxArray *d;
    k10_int("struct.nfields", mxGetNumberOfFields(s));
    k10_say("struct.name0", mxGetFieldNameByNumber(s, 0));
    k10_say("struct.name1", mxGetFieldNameByNumber(s, 1));
    k10_int("struct.number.b", mxGetFieldNumber(s, "b"));
    k10_int("struct.number.c", mxGetFieldNumber(s, "c"));
    k10_int("struct.number.A", mxGetFieldNumber(s, "A"));
    mxSetField(s, 0, "a", mxCreateDoubleScalar(1));
    mxSetField(s, 1, "b", mxCreateString("x"));
    k10_line("struct.get.unset", mxGetField(s, 0, "b"));
    k10_line("struct.get.set", mxGetField(s, 1, "b"));
    k10_line("struct.get.nosuch", mxGetField(s, 0, "zz"));
    k10_line("struct.after.set", s);
    k10_int("struct.add.c", mxAddField(s, "c"));
    k10_int("struct.add.a", mxAddField(s, "a"));
    k10_int("struct.add.long", mxAddField(s, "a234567890123456789012345678901234567890123456789012345678901234"));
    k10_int("struct.add.63", mxAddField(s, "b23456789012345678901234567890123456789012345678901234567890123"));
    k10_line("struct.after.add", s);
    mxSetFieldByNumber(s, 1, 2, mxCreateDoubleScalar(3));
    mxRemoveField(s, 0);
    k10_line("struct.after.remove", s);
    d = mxDuplicateArray(s);
    mxDestroyArray(mxGetField(d, 1, "b"));
    mxSetField(d, 1, "b", mxCreateDoubleScalar(42));
    k10_line("struct.dup", d);
    k10_line("struct.original", s);
    mxDestroyArray(d);
    mxDestroyArray(s);
}

static void Cells(void)
{
    mxArray *c = mxCreateCellMatrix(1, 3);
    mxArray *d;
    mxSetCell(c, 0, mxCreateDoubleScalar(5));
    mxSetCell(c, 2, mxCreateString("z"));
    k10_line("cell.set", c);
    k10_line("cell.get.unset", mxGetCell(c, 1));
    d = mxDuplicateArray(c);
    mxDestroyArray(mxGetCell(d, 0));
    mxSetCell(d, 0, mxCreateDoubleScalar(6));
    k10_line("cell.dup", d);
    k10_line("cell.original", c);
    {
        mxArray *nest = mxCreateCellMatrix(1, 1);
        mxSetCell(nest, 0, c);
        k10_line("cell.nested", nest);
        d = mxDuplicateArray(nest);
        k10_line("cell.nested.dup", d);
        mxDestroyArray(d);
        mxDestroyArray(nest);
    }
}

static void Reshape(void)
{
    mxArray *a = Row(1, 6);
    static const mwSize d16[] = { 1, 6 }, d123[] = { 1, 2, 3 }, d611[] = { 6, 1, 1 }, d6[] = { 6 };
    mxSetM(a, 2); mxSetN(a, 3); k10_line("reshape.mn", a);
    k10_int("reshape.dims16.result", mxSetDimensions(a, d16, 2)); k10_line("reshape.dims16", a);
    k10_int("reshape.dims123.result", mxSetDimensions(a, d123, 3)); k10_line("reshape.dims123", a);
    k10_int("reshape.dims611.result", mxSetDimensions(a, d611, 3)); k10_line("reshape.dims611", a);
    k10_int("reshape.dims6.result", mxSetDimensions(a, d6, 1)); k10_line("reshape.dims6", a);
    mxSetM(a, 3); mxSetN(a, 2); k10_line("reshape.setmn32", a);
    {
        mxArray *nd = mxCreateNumericArray(3, d123, mxDOUBLE_CLASS, mxREAL);
        mxSetN(nd, 6); k10_shape_line("reshape.setn.nd", nd);
        mxDestroyArray(nd);
    }
    mxDestroyArray(a);
}

static void DataReplacement(void)
{
    mxArray *a = mxCreateDoubleMatrix(0, 0, mxREAL);
    double *p = (double *)mxMalloc(3 * sizeof(double));
    p[0] = 1; p[1] = 2; p[2] = 3;
#if MX_HAS_INTERLEAVED_COMPLEX
    k10_int("data.setdoubles.result", mxSetDoubles(a, p));
#else
    mxSetPr(a, p);
#endif
    mxSetM(a, 1); mxSetN(a, 3);
    k10_line("data.setpr", a);
    mxDestroyArray(a);

    a = mxCreateNumericMatrix(0, 0, mxINT32_CLASS, mxREAL);
    {
        int *q = (int *)mxCalloc(2, sizeof(int));
        q[0] = -1; q[1] = 4;
        mxSetData(a, q);
        mxSetM(a, 2); mxSetN(a, 1);
        k10_line("data.setdata", a);
    }
    mxDestroyArray(a);

#if MX_HAS_INTERLEAVED_COMPLEX
    a = Row(1, 2);
    k10_int("data.makecomplex.result", mxMakeArrayComplex(a)); k10_line("data.makecomplex", a);
    ((mxComplexDouble *)mxGetComplexDoubles(a))[1].imag = 5;
    k10_int("data.makecomplex.again", mxMakeArrayComplex(a));
    k10_int("data.makereal.result", mxMakeArrayReal(a)); k10_line("data.makereal", a);
    k10_int("data.makereal.again", mxMakeArrayReal(a));
    k10_line("data.getdoubles.real", mxGetDoubles(a) ? a : NULL);
    k10_int("data.getcomplexdoubles.real.null", mxGetComplexDoubles(a) == NULL);
    mxDestroyArray(a);
    a = mxCreateString("ab");
    k10_int("data.makecomplex.char", mxMakeArrayComplex(a));
    mxDestroyArray(a);
    a = mxCreateNumericMatrix(1, 2, mxINT8_CLASS, mxREAL);
    k10_int("data.getdoubles.int8.null", mxGetDoubles(a) == NULL);
    k10_int("data.getint8s.int8.null", mxGetInt8s(a) == NULL);
    k10_int("data.makecomplex.int8", mxMakeArrayComplex(a)); k10_line("data.int8.complex", a);
    k10_int("data.getcomplexint8s.null", mxGetComplexInt8s(a) == NULL);
    mxDestroyArray(a);
    a = mxCreateDoubleMatrix(1, 1, mxCOMPLEX);
    k10_int("data.getdoubles.complex.null", mxGetDoubles(a) == NULL);
    mxDestroyArray(a);
    a = mxCreateLogicalScalar(true);
    k10_int("data.getdoubles.logical.null", mxGetDoubles(a) == NULL);
    mxDestroyArray(a);
    {
        mxComplexDouble *z = (mxComplexDouble *)mxMalloc(2 * sizeof(mxComplexDouble));
        a = mxCreateDoubleMatrix(1, 2, mxCOMPLEX);
        z[0].real = 1; z[0].imag = 2; z[1].real = 3; z[1].imag = 4;
        k10_int("data.setcomplexdoubles.result", mxSetComplexDoubles(a, z)); k10_line("data.setcomplexdoubles", a);
        mxDestroyArray(a);
    }
#else
    a = Row(1, 2);
    k10_int("data.getpi.real.null", mxGetPi(a) == NULL);
    {
        double *im = (double *)mxCalloc(2, sizeof(double));
        im[1] = 5;
        mxSetPi(a, im);
        k10_line("data.setpi", a);
        mxSetPi(a, NULL);
        k10_line("data.setpi.null", a);
        mxFree(im);
    }
    mxDestroyArray(a);
    a = mxCreateNumericMatrix(1, 2, mxINT16_CLASS, mxREAL);
    {
        short *im = (short *)mxCalloc(2, sizeof(short));
        im[0] = 7;
        mxSetImagData(a, im);
        k10_line("data.setimagdata", a);
    }
    mxDestroyArray(a);
#endif
}

static void Memory(void)
{
    unsigned char *p = (unsigned char *)mxCalloc(4, 2);
    int zero = 1, i;
    for (i = 0; i < 8; i++)
        zero &= p[i] == 0;
    k10_int("memory.calloc.zero", zero);
    p[0] = 9;
    p = (unsigned char *)mxRealloc(p, 64);
    k10_int("memory.realloc.kept", p[0]);
    mxFree(p);
    mxFree(NULL);
    k10_int("memory.free.null", 1);
    mxDestroyArray(NULL);
    k10_int("memory.destroy.null", 1);
}

static void Risky(int n)
{
    mxArray *a, *s, *c;
    const char *dup[] = { "a", "a" };
    const char *bad[] = { "1bad" };
    const char *spaced[] = { "a b" };
    printf("risky %d\n", n);
    fflush(stdout);
    switch (n)
    {
    case 1: c = mxCreateCellMatrix(2, 2); k10_line("risky.getcell.range", mxGetCell(c, 4)); break;
    case 2: s = mxCreateStructMatrix(1, 2, 2, Names2); k10_line("risky.getfield.range", mxGetField(s, 2, "a")); break;
    case 3: c = mxCreateCellMatrix(1, 1); mxSetCell(c, 10, mxCreateDoubleScalar(1)); k10_line("risky.setcell.range", c); break;
    case 4: s = mxCreateStructMatrix(1, 1, 2, Names2); k10_line("risky.getfieldbynumber.range", mxGetFieldByNumber(s, 0, 5)); break;
    case 5: k10_line("risky.struct.duplicate", mxCreateStructMatrix(1, 1, 2, dup)); break;
    case 6: k10_line("risky.struct.invalid", mxCreateStructMatrix(1, 1, 1, bad)); break;
    case 7: s = mxCreateStructMatrix(1, 1, 2, Names2); k10_int("risky.addfield.invalid", mxAddField(s, "1bad")); k10_line("risky.addfield.invalid.after", s); break;
    case 8: k10_line("risky.numeric.cellclass", mxCreateNumericMatrix(2, 2, mxCELL_CLASS, mxREAL)); break;
    case 9: k10_line("risky.numeric.logicalclass", mxCreateNumericMatrix(1, 2, mxLOGICAL_CLASS, mxREAL)); break;
    case 10: k10_line("risky.numeric.charclass", mxCreateNumericMatrix(1, 2, mxCHAR_CLASS, mxREAL)); break;
    case 11:
        a = mxCreateNumericMatrix(1, 2, mxINT8_CLASS, mxREAL);
        k10_int("risky.getpr.int8.null", mxGetPr(a) == NULL);
        break;
    case 12:
        a = mxCreateDoubleMatrix(1, 2, mxCOMPLEX);
        k10_int("risky.getpr.complex.null", mxGetPr(a) == NULL);
        break;
    case 13:
        a = Row(1, 6);
        {
            static const mwSize d44[] = { 4, 4 };
            k10_int("risky.setdims.grow.result", mxSetDimensions(a, d44, 2));
            k10_shape_line("risky.setdims.grow", a);
        }
        break;
    case 14: s = mxCreateStructMatrix(1, 1, 2, Names2); mxSetField(s, 0, "nosuch", mxCreateDoubleScalar(1)); k10_line("risky.setfield.nosuch", s); break;
    case 15: s = mxCreateStructMatrix(1, 1, 2, Names2); k10_say("risky.fieldname.range", mxGetFieldNameByNumber(s, 5)); break;
    case 16: a = mxCreateDoubleScalar(1); k10_int("risky.nfields.double", mxGetNumberOfFields(a)); k10_int("risky.fieldnumber.double", mxGetFieldNumber(a, "a")); break;
    case 17: a = mxCreateDoubleScalar(1); k10_line("risky.getcell.double", mxGetCell(a, 0)); break;
    case 18: a = mxCreateDoubleScalar(1); k10_line("risky.getfield.double", mxGetField(a, 0, "a")); break;
    case 19: s = mxCreateStructMatrix(1, 1, 2, Names2); mxRemoveField(s, 7); k10_line("risky.removefield.range", s); break;
    case 20: k10_line("risky.struct.spaced", mxCreateStructMatrix(1, 1, 1, spaced)); break;
    case 21: s = mxCreateStructMatrix(1, 1, 2, Names2); mxSetFieldByNumber(s, 0, 4, mxCreateDoubleScalar(1)); k10_line("risky.setfieldbynumber.range", s); break;
    case 22: a = mxCreateDoubleScalar(1); k10_line("risky.duplicate.null", mxDuplicateArray(NULL)); (void)a; break;
    case 23: a = mxCreateString("ab"); k10_int("risky.getlogicals.char.null", mxGetLogicals(a) == NULL); break;
    case 24: a = mxCreateDoubleScalar(1); k10_int("risky.getchars.double.null", mxGetChars(a) == NULL); break;
    case 25: a = mxCreateDoubleScalar(1); k10_int("risky.getir.full.null", mxGetIr(a) == NULL); k10_int("risky.getnzmax.full", (long long)mxGetNzmax(a)); break;
    default: printf("risky %d: no such case\n", n); break;
    }
    printf("risky %d: returned\n", n);
    fflush(stdout);
}

int main(int argc, char **argv)
{
    if (!mclmcrInitialize())
    {
        printf("mclmcrInitialize: false\n");
        return 1;
    }

    if (!mclInitializeApplication(NULL, 0))
    {
        printf("mclInitializeApplication: false\n");
        return 1;
    }

    printf("model: %s\n", K10_MODEL);
    fflush(stdout);
    if (argc > 2 && strcmp(argv[1], "risky") == 0)
    {
        Risky(atoi(argv[2]));
    }
    else
    {
        Create();
        Queries();
        Strings();
        Structs();
        Cells();
        Reshape();
        DataReplacement();
        Memory();
    }

    mclTerminateApplication();
    printf("done\n");
    return 0;
}
