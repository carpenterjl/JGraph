/*
 * k10feval: a client of the C shared library k10lib (mcc -W lib:k10lib -T link:lib src\k10\*.m),
 * for what crosses the runtime's call boundary (compiler and SDK plan, stage K10): mxArrays into
 * MATLAB and back, outputs asked for and given, errors, the stack trace, the print and error
 * handlers, objects and their properties.
 *
 *   k10feval           every safe question
 *   k10feval risky N   risky question N alone, in a process of its own
 *
 * R2025b's generated k10lib.c is what this links against in both runs: built by R2025b's mcc,
 * and compiled by run-k10.ps1 against the JGraph Runtime's headers with JGraph's archive appended.
 */
#include "k10lib.h"
#include "k10common.h"

static mxArray *Str(const char *s) { return mxCreateString(s); }

static mxArray *Doubles(size_t m, size_t n, const double *values)
{
    mxArray *a = mxCreateDoubleMatrix(m, n, mxREAL);
    memcpy(mxGetData(a), values, m * n * sizeof(double));
    return a;
}

static mxArray *Numeric1(mxClassID id, const void *value, size_t count)
{
    mxArray *a = mxCreateNumericMatrix(1, count, id, mxREAL);
    memcpy(mxGetData(a), value, count * k10_base_size(id));
    return a;
}

static mxArray *ComplexRow(void)
{
    mxArray *a = mxCreateDoubleMatrix(1, 2, mxCOMPLEX);
#if MX_HAS_INTERLEAVED_COMPLEX
    mxComplexDouble *z = mxGetComplexDoubles(a);
    z[0].real = 1; z[0].imag = 2; z[1].real = 3; z[1].imag = -4;
#else
    double *re = mxGetPr(a), *im = mxGetPi(a);
    re[0] = 1; im[0] = 2; re[1] = 3; im[1] = -4;
#endif
    return a;
}

static mxArray *Sparse(void)
{
    mxArray *a = mxCreateSparse(3, 3, 3, mxREAL);
    mwIndex *ir = mxGetIr(a), *jc = mxGetJc(a);
    double *pr = (double *)mxGetData(a);
    jc[0] = 0; jc[1] = 1; jc[2] = 2; jc[3] = 2;
    ir[0] = 0; ir[1] = 2;
    pr[0] = 5; pr[1] = 6;
    return a;
}

static mxArray *SparseComplex(void)
{
    mxArray *a = mxCreateSparse(2, 2, 2, mxCOMPLEX);
    mwIndex *ir = mxGetIr(a), *jc = mxGetJc(a);
    jc[0] = 0; jc[1] = 2; jc[2] = 2;
    ir[0] = 0; ir[1] = 1;
#if MX_HAS_INTERLEAVED_COMPLEX
    {
        mxComplexDouble *z = mxGetComplexDoubles(a);
        z[0].real = 1; z[0].imag = 1; z[1].real = 2; z[1].imag = 0;
    }
#else
    {
        double *re = mxGetPr(a), *im = mxGetPi(a);
        re[0] = 1; im[0] = 1; re[1] = 2; im[1] = 0;
    }
#endif
    return a;
}

static mxArray *SparseLogical(void)
{
    mxArray *a = mxCreateSparseLogicalMatrix(2, 2, 2);
    mwIndex *ir = mxGetIr(a), *jc = mxGetJc(a);
    mxLogical *l = mxGetLogicals(a);
    jc[0] = 0; jc[1] = 1; jc[2] = 2;
    ir[0] = 0; ir[1] = 1;
    l[0] = true; l[1] = true;
    return a;
}

static mxArray *Wide(void)
{
    static const mwSize d15[] = { 1, 5 };
    mxArray *a = mxCreateCharArray(2, d15);
    mxChar *c = mxGetChars(a);
    c[0] = 'c'; c[1] = 0xE9; c[2] = 0x20AC; c[3] = 0xD83D; c[4] = 0xDE00;
    return a;
}

static mxArray *Input(int k, const char **name)
{
    static const double six[] = { 1, 2, 3, 4, 5, 6 };
    static const double specials[] = { 0, 0, 0 };
    switch (k)
    {
    case 0: *name = "scalar"; return mxCreateDoubleScalar(2.5);
    case 1: *name = "row"; return Doubles(1, 3, six);
    case 2: *name = "matrix"; return Doubles(2, 3, six);
    case 3:
    {
        static const mwSize d222[] = { 2, 2, 2 };
        static const short v[] = { 1, -2, 3, -4, 5, -6, 7, -8 };
        mxArray *a = mxCreateNumericArray(3, d222, mxINT16_CLASS, mxREAL);
        *name = "nd.int16";
        memcpy(mxGetData(a), v, sizeof(v));
        return a;
    }
    case 4: *name = "complex"; return ComplexRow();
    case 5: { static const signed char v[] = { -128, 127 }; *name = "int8"; return Numeric1(mxINT8_CLASS, v, 2); }
    case 6: { static const unsigned char v[] = { 0, 255 }; *name = "uint8"; return Numeric1(mxUINT8_CLASS, v, 2); }
    case 7: { static const short v[] = { -32768, 32767 }; *name = "int16"; return Numeric1(mxINT16_CLASS, v, 2); }
    case 8: { static const unsigned short v[] = { 0, 65535 }; *name = "uint16"; return Numeric1(mxUINT16_CLASS, v, 2); }
    case 9: { static const int v[] = { -2147483647 - 1, 2147483647 }; *name = "int32"; return Numeric1(mxINT32_CLASS, v, 2); }
    case 10: { static const unsigned int v[] = { 0, 4294967295u }; *name = "uint32"; return Numeric1(mxUINT32_CLASS, v, 2); }
    case 11: { static const long long v[] = { -9223372036854775807LL - 1, 9223372036854775807LL }; *name = "int64"; return Numeric1(mxINT64_CLASS, v, 2); }
    case 12: { static const unsigned long long v[] = { 0, 18446744073709551615ULL }; *name = "uint64"; return Numeric1(mxUINT64_CLASS, v, 2); }
    case 13: { static const unsigned long long v[] = { 9007199254740993ULL }; *name = "uint64.odd"; return Numeric1(mxUINT64_CLASS, v, 1); }
    case 14: { static const float v[] = { 1.5f, -0.25f }; *name = "single"; return Numeric1(mxSINGLE_CLASS, v, 2); }
    case 15:
    {
        mxArray *a = mxCreateLogicalMatrix(1, 3);
        mxLogical *l = mxGetLogicals(a);
        *name = "logical";
        l[0] = true; l[2] = true;
        return a;
    }
    case 16: *name = "true"; return mxCreateLogicalScalar(true);
    case 17: *name = "char"; return Str("hello");
    case 18:
    {
        const char *strs[] = { "a", "bcd", "ef" };
        *name = "charmatrix";
        return mxCreateCharMatrixFromStrings(3, strs);
    }
    case 19: *name = "emptychar"; return Str("");
    case 20: *name = "wide"; return Wide();
    case 21:
    {
        mxArray *c = mxCreateCellMatrix(1, 3);
        *name = "cell.null";
        mxSetCell(c, 0, mxCreateDoubleScalar(1));
        mxSetCell(c, 1, Str("two"));
        return c;
    }
    case 22:
    {
        mxArray *c = mxCreateCellMatrix(1, 1), *inner = mxCreateCellMatrix(1, 1);
        *name = "cell.nested";
        mxSetCell(inner, 0, mxCreateDoubleScalar(1));
        mxSetCell(c, 0, inner);
        return c;
    }
    case 23:
    {
        const char *names[] = { "a", "b" };
        mxArray *s = mxCreateStructMatrix(1, 2, 2, names);
        *name = "struct.null";
        mxSetField(s, 0, "a", mxCreateDoubleScalar(1));
        mxSetField(s, 1, "b", Str("x"));
        return s;
    }
    case 24: *name = "struct.nofields"; return mxCreateStructMatrix(1, 1, 0, NULL);
    case 25: { const char *names[] = { "a" }; *name = "struct.empty"; return mxCreateStructMatrix(0, 0, 1, names); }
    case 26: *name = "sparse"; return Sparse();
    case 27: *name = "sparse.complex"; return SparseComplex();
    case 28: *name = "sparse.logical"; return SparseLogical();
    case 29: *name = "empty"; return mxCreateDoubleMatrix(0, 0, mxREAL);
    case 30: *name = "empty03"; return mxCreateDoubleMatrix(0, 3, mxREAL);
    case 31: *name = "empty10"; return mxCreateDoubleMatrix(1, 0, mxREAL);
    case 32:
    {
        mxArray *a = Doubles(1, 3, specials);
        double *p = (double *)mxGetData(a);
        *name = "specials";
        p[0] = mxGetNaN(); p[1] = -mxGetInf(); p[2] = -0.0;
        return a;
    }
    case 33: *name = "cell.empty"; return mxCreateCellMatrix(0, 0);
    case 34: *name = "cell.1x0"; return mxCreateCellMatrix(1, 0);
    case 35:
    {
        static const mwSize d2111[] = { 2, 1, 1, 1 };
        mxArray *a = mxCreateNumericArray(4, d2111, mxDOUBLE_CLASS, mxREAL);
        *name = "trailing";
        return a;
    }
    case 36: *name = "false"; return mxCreateLogicalScalar(false);
    case 37:
    {
        mxArray *a = mxCreateNumericMatrix(1, 1, mxINT8_CLASS, mxCOMPLEX);
#if MX_HAS_INTERLEAVED_COMPLEX
        mxComplexInt8 *z = mxGetComplexInt8s(a);
        z[0].real = 3; z[0].imag = -1;
#else
        ((signed char *)mxGetData(a))[0] = 3;
        ((signed char *)mxGetImagData(a))[0] = -1;
#endif
        *name = "complex.int8";
        return a;
    }
    case 38:
    {
        mxArray *a = mxCreateDoubleMatrix(1, 2, mxCOMPLEX);
        *name = "complex.zeroimag";
        ((double *)mxGetData(a))[0] = 4;
        return a;
    }
    case 39:
    {
        static const mwSize d0[] = { 0, 2, 0 };
        *name = "char.empty.nd";
        return mxCreateCharArray(3, d0);
    }
    default: return NULL;
    }
}

static const char *Kinds[] = {
    "double", "row", "matrix", "nd", "nd1", "empty", "empty03", "empty10", "negzero", "specials", "single",
    "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "complex", "complexscalar",
    "complexsingle", "complexint", "logical", "logicalscalar", "char", "charmatrix", "emptychar", "charnd",
    "unicode", "cell", "nestedcell", "emptycell", "cell03", "struct", "structarray", "struct2x2",
    "emptystruct", "nofields", "string", "strings", "stringmissing", "sparse", "sparsecomplex",
    "sparselogical", "sparseempty", "handle", "anonymous", "object", "value", "table", "datetime",
    "duration", "categorical", "map", "dictionary", "cellstring", "structstring", NULL
};

static mxArray *Call1(bool (*fn)(int, mxArray **, int, mxArray **), mxArray *x, bool *ok)
{
    mxArray *out[1] = { NULL };
    mxArray *in[1];
    in[0] = x;
    *ok = fn(1, out, 1, in);
    return out[0];
}

static void Info(const char *label, mxArray *x)
{
    bool ok;
    mxArray *s = Call1(mlxK10info, x, &ok);
    char *text = ok && s != NULL ? mxArrayToString(s) : NULL;
    if (!ok)
        k10_say(label, "(call failed)");
    else
        k10_say(label, text);
    if (text != NULL)
        mxFree(text);
    if (s != NULL)
        mxDestroyArray(s);
}

static void Stack(const char *label)
{
    char **stack = NULL;
    char key[80];
    int depth = mclGetStackTrace(&stack), i;
    sprintf(key, "%s.depth", label);
    k10_int(key, depth);
    for (i = 0; i < depth; i++)
    {
        sprintf(key, "%s.frame%d", label, i);
        k10_say(key, stack[i]);
    }
    if (depth > 0)
        mclFreeStackTrace(&stack, depth);
    sprintf(key, "%s.lastmessage", label);
    k10_say(key, mclGetLastErrorMessage());
}

static void RoundTrips(void)
{
    int k;
    char label[96];
    for (k = 0;; k++)
    {
        const char *name = NULL;
        mxArray *x = Input(k, &name), *y;
        bool ok;
        if (x == NULL)
            break;
        sprintf(label, "in.%s", name);
        k10_line(label, x);
        y = Call1(mlxK10echo, x, &ok);
        sprintf(label, "echo.%s", name);
        if (!ok)
            k10_say(label, "(call failed)");
        else
            k10_line(label, y);
        sprintf(label, "info.%s", name);
        Info(label, x);
        if (y != NULL)
            mxDestroyArray(y);
        mxDestroyArray(x);
    }
}

static void Makes(void)
{
    int k;
    char label[96];
    for (k = 0; Kinds[k] != NULL; k++)
    {
        bool ok;
        mxArray *kind = Str(Kinds[k]);
        mxArray *v = Call1(mlxK10make, kind, &ok), *w;
        sprintf(label, "make.%s", Kinds[k]);
        if (!ok)
        {
            k10_say(label, "(call failed)");
            mxDestroyArray(kind);
            continue;
        }

        k10_line(label, v);
        sprintf(label, "make.%s.info", Kinds[k]);
        Info(label, v);
        w = Call1(mlxK10echo, v, &ok);
        sprintf(label, "make.%s.echo", Kinds[k]);
        if (!ok)
            k10_say(label, "(call failed)");
        else
            k10_line(label, w);
        if (w != NULL)
        {
            sprintf(label, "make.%s.echo.info", Kinds[k]);
            Info(label, w);
            mxDestroyArray(w);
        }

        sprintf(label, "make.%s.dup", Kinds[k]);
        w = mxDuplicateArray(v);
        k10_line(label, w);
        sprintf(label, "make.%s.dup.info", Kinds[k]);
        Info(label, w);
        mxDestroyArray(w);
        mxDestroyArray(v);
        mxDestroyArray(kind);
    }
}

static void Objects(void)
{
    bool ok;
    mxArray *kind = Str("object");
    mxArray *obj = Call1(mlxK10make, kind, &ok), *v, *dup;
    k10_line("object", obj);
    k10_int("object.isclass.k10obj", mxIsClass(obj, "k10obj"));
    k10_int("object.isclass.handle", mxIsClass(obj, "handle"));
    k10_line("object.get.Value", v = mxGetProperty(obj, 0, "Value")); if (v) mxDestroyArray(v);
    k10_line("object.get.Label", v = mxGetProperty(obj, 0, "Label")); if (v) mxDestroyArray(v);
    v = mxCreateDoubleScalar(11);
    mxSetProperty(obj, 0, "Value", v);
    mxDestroyArray(v);
    k10_line("object.set.Value", v = mxGetProperty(obj, 0, "Value")); if (v) mxDestroyArray(v);
    Info("object.set.info", obj);
    dup = mxDuplicateArray(obj);
    v = mxCreateDoubleScalar(12);
    mxSetProperty(dup, 0, "Value", v);
    mxDestroyArray(v);
    k10_line("object.dup.set.original", v = mxGetProperty(obj, 0, "Value")); if (v) mxDestroyArray(v);
    mxDestroyArray(dup);
    mxDestroyArray(obj);
    mxDestroyArray(kind);

    kind = Str("value");
    obj = Call1(mlxK10make, kind, &ok);
    k10_line("value", obj);
    v = mxCreateDoubleScalar(9);
    mxSetProperty(obj, 0, "Value", v);
    mxDestroyArray(v);
    k10_line("value.set.Value", v = mxGetProperty(obj, 0, "Value")); if (v) mxDestroyArray(v);
    Info("value.set.info", obj);
    dup = mxDuplicateArray(obj);
    v = mxCreateDoubleScalar(10);
    mxSetProperty(dup, 0, "Value", v);
    mxDestroyArray(v);
    k10_line("value.dup.set.original", v = mxGetProperty(obj, 0, "Value")); if (v) mxDestroyArray(v);
    k10_line("value.dup.set.dup", v = mxGetProperty(dup, 0, "Value")); if (v) mxDestroyArray(v);
    mxDestroyArray(dup);
    mxDestroyArray(obj);
    mxDestroyArray(kind);

    kind = Str("struct");
    obj = Call1(mlxK10make, kind, &ok);
    k10_line("struct.getproperty", v = mxGetProperty(obj, 0, "name")); if (v) mxDestroyArray(v);
    mxDestroyArray(obj);
    mxDestroyArray(kind);
}

static void Outputs(void)
{
    mxArray *x = mxCreateDoubleScalar(3);
    mxArray *out[3];
    mxArray *in[3];
    bool ok;
    int nlhs, nrhs, k;
    char label[64];

    for (nlhs = 0; nlhs <= 3; nlhs++)
    {
        out[0] = out[1] = out[2] = NULL;
        in[0] = x;
        ok = mlxK10two(nlhs, out, 1, in);
        sprintf(label, "two.nlhs%d.ok", nlhs);
        k10_int(label, ok);
        for (k = 0; k < 3; k++)
        {
            sprintf(label, "two.nlhs%d.out%d", nlhs, k);
            k10_line(label, out[k]);
            if (out[k] != NULL)
                mxDestroyArray(out[k]);
        }
    }

    for (nlhs = 0; nlhs <= 2; nlhs++)
    {
        for (nrhs = 0; nrhs <= 2; nrhs++)
        {
            out[0] = out[1] = out[2] = NULL;
            in[0] = in[1] = in[2] = x;
            ok = mlxK10nargs(nlhs, out, nrhs, in);
            sprintf(label, "nargs.%d.%d.ok", nlhs, nrhs);
            k10_int(label, ok);
            for (k = 0; k < 2; k++)
            {
                sprintf(label, "nargs.%d.%d.out%d", nlhs, nrhs, k);
                k10_line(label, out[k]);
                if (out[k] != NULL)
                    mxDestroyArray(out[k]);
            }
        }
    }

    out[0] = NULL;
    in[0] = x;
    k10_int("none.nlhs0.ok", mlxK10none(0, out, 1, in));
    k10_line("none.nlhs0.out0", out[0]);
    k10_int("none.nlhs1.ok", mlxK10none(1, out, 1, in));
    k10_line("none.nlhs1.out0", out[0]);

    in[0] = x; in[1] = x; in[2] = x;
    out[0] = NULL;
    k10_int("addtwo.three.ok", mlxK10addtwo(1, out, 3, in));
    Stack("addtwo.three");
    k10_int("addtwo.one.ok", mlxK10addtwo(1, out, 1, in));
    k10_line("addtwo.one.out0", out[0]);
    Stack("addtwo.one");
    if (out[0] != NULL)
        mxDestroyArray(out[0]);

    {
        mxArray *a = NULL, *b = NULL, *sentinel = mxCreateString("sentinel");
        k10_int("mlf.two.1.ok", mlfK10two(1, &a, &b, x));
        k10_line("mlf.two.1.a", a);
        k10_line("mlf.two.1.b", b);
        if (a) mxDestroyArray(a);
        a = NULL;
        b = sentinel;
        k10_int("mlf.two.1s.ok", mlfK10two(1, &a, &b, x));
        k10_int("mlf.two.1s.b.kept", b == sentinel);
        k10_line("mlf.two.1s.b", b);
        if (a) mxDestroyArray(a);
        a = NULL;
        b = NULL;
        k10_int("mlf.two.2.ok", mlfK10two(2, &a, &b, x));
        k10_line("mlf.two.2.a", a);
        k10_line("mlf.two.2.b", b);
        if (a) mxDestroyArray(a);
        if (b) mxDestroyArray(b);
        a = NULL;
        k10_int("mlf.addtwo.0.ok", mlfK10addtwo(0, &a, x, x));
        k10_line("mlf.addtwo.0.c", a);
        if (a) mxDestroyArray(a);
        a = NULL;
        k10_int("mlf.addtwo.1.ok", mlfK10addtwo(1, &a, x, x));
        k10_line("mlf.addtwo.1.c", a);
        if (a) mxDestroyArray(a);
        mxDestroyArray(sentinel);
    }

    {
        mxArray *vin = mxCreateCellMatrix(1, 3), *none = mxCreateCellMatrix(0, 0), *vout = NULL, *a = NULL;
        mxSetCell(vin, 0, mxDuplicateArray(x));
        mxSetCell(vin, 1, mxCreateString("s"));
        mxSetCell(vin, 2, mxCreateDoubleScalar(7));
        k10_int("mlf.nargs.2.ok", mlfK10nargs(2, &vout, vin));
        k10_line("mlf.nargs.2.vout", vout);
        if (vout) mxDestroyArray(vout);
        vout = NULL;
        k10_int("mlf.nargs.0.ok", mlfK10nargs(0, &vout, vin));
        k10_line("mlf.nargs.0.vout", vout);
        if (vout) mxDestroyArray(vout);
        vout = NULL;
        k10_int("mlf.nargs.none.ok", mlfK10nargs(1, &vout, none));
        k10_line("mlf.nargs.none.vout", vout);
        if (vout) mxDestroyArray(vout);
        vout = NULL;
        k10_int("mlf.echo.1.ok", mlfK10echo(1, &vout, vin));
        k10_line("mlf.echo.1.vout", vout);
        if (vout) mxDestroyArray(vout);
        vout = NULL;
        k10_int("mlf.mixed.3.ok", mlfK10mixed(3, &a, &vout, x, vin));
        k10_line("mlf.mixed.3.a", a);
        k10_line("mlf.mixed.3.vout", vout);
        if (a) mxDestroyArray(a);
        if (vout) mxDestroyArray(vout);
        a = NULL;
        vout = NULL;
        k10_int("mlf.mixed.1.ok", mlfK10mixed(1, &a, &vout, x, none));
        k10_line("mlf.mixed.1.a", a);
        k10_line("mlf.mixed.1.vout", vout);
        if (a) mxDestroyArray(a);
        if (vout) mxDestroyArray(vout);
        a = NULL;
        vout = NULL;
        k10_int("mlf.obj.ok", mlfK10obj(1, &vout, vin));
        k10_line("mlf.obj.vout", vout);
        if (vout) mxDestroyArray(vout);
        mxDestroyArray(vin);
        mxDestroyArray(none);
    }

    {
        mxArray *in[2], *out[3] = { NULL, NULL, NULL };
        in[0] = x; in[1] = x;
        k10_int("mixed.mlx.ok", mlxK10mixed(3, out, 2, in));
        k10_line("mixed.mlx.out0", out[0]);
        k10_line("mixed.mlx.out1", out[1]);
        k10_line("mixed.mlx.out2", out[2]);
        if (out[0]) mxDestroyArray(out[0]);
        if (out[1]) mxDestroyArray(out[1]);
        if (out[2]) mxDestroyArray(out[2]);
    }

    mxDestroyArray(x);
}

static void Errors(void)
{
    static const char *kinds[] = { "id", "noid", "lines", "nested", "cause", "undefined", "index", NULL };
    int k;
    char label[64];
    for (k = 0; kinds[k] != NULL; k++)
    {
        mxArray *kind = Str(kinds[k]);
        mxArray *in[1];
        bool ok;
        in[0] = kind;
        sprintf(label, "err.%s", kinds[k]);
        printf("%s: calling\n", label);
        fflush(stdout);
        ok = mlxK10err(0, NULL, 1, in);
        sprintf(label, "err.%s.ok", kinds[k]);
        k10_int(label, ok);
        sprintf(label, "err.%s", kinds[k]);
        Stack(label);
        mxDestroyArray(kind);
    }

    k10_say("printstack", "next");
    k10libPrintStackTrace();
    fflush(stderr);
}

static void Printing(void)
{
    mxArray *n = mxCreateDoubleScalar(4);
    mxArray *in[1];
    in[0] = n;
    k10_say("print", "calling");
    k10_int("print.ok", mlxK10print(0, NULL, 1, in));
    mxDestroyArray(n);
}

static void Risky(int n)
{
    mxArray *in[2] = { NULL, NULL };
    mxArray *out[2] = { NULL, NULL };
    printf("risky %d\n", n);
    fflush(stdout);
    switch (n)
    {
    case 1: k10_int("risky.nullinput.ok", mlxK10info(1, out, 1, in)); k10_line("risky.nullinput.out", out[0]); break;
    case 2: k10libTerminate(); in[0] = mxCreateDoubleScalar(1); k10_int("risky.afterterminate.ok", mlxK10echo(1, out, 1, in)); k10_line("risky.afterterminate.out", out[0]); break;
    case 3: k10_int("risky.reinitialize", k10libInitialize()); break;
    case 4: k10_int("risky.mlf.nullinput.ok", mlfK10addtwo(1, out, NULL, NULL)); k10_line("risky.mlf.nullinput.out", out[0]); break;
    case 5: in[0] = mxCreateDoubleScalar(1); k10_int("risky.negative.nlhs.ok", mlxK10echo(-1, out, 1, in)); k10_line("risky.negative.nlhs.out", out[0]); break;
    case 6: k10_int("risky.mlf.nulloutput.ok", mlfK10addtwo(1, NULL, mxCreateDoubleScalar(1), mxCreateDoubleScalar(2))); break;
    case 7: in[0] = mxCreateDoubleScalar(1); k10_int("risky.mlf.varargin.notcell.ok", mlfK10nargs(1, out, in[0])); k10_line("risky.mlf.varargin.notcell.out", out[0]); break;
    case 8: in[0] = mxCreateDoubleScalar(1); k10_int("risky.mlx.nlhs.too.many.ok", mlxK10echo(2, out, 1, in)); k10_line("risky.mlx.nlhs.too.many.out1", out[1]); break;
    case 9:
    case 10:
    {
        bool ok;
        mxArray *kind = Str("object"), *obj = Call1(mlxK10make, kind, &ok);
        const char *name = n == 9 ? "Nope" : "Hidden";
        printf("risky.getproperty.%s: ", name);
        k10_describe(mxGetProperty(obj, 0, name));
        printf("\n");
        break;
    }
    case 11:
    case 12:
    {
        const char *bad[] = { "1bad" };
        const char *dup[] = { "a", "a" };
        mxArray *s = n == 11 ? mxCreateStructMatrix(1, 1, 1, bad) : mxCreateStructMatrix(1, 1, 2, dup);
        mxSetFieldByNumber(s, 0, 0, mxCreateDoubleScalar(1));
        Info(n == 11 ? "risky.struct.invalidname.info" : "risky.struct.duplicate.info", s);
        out[0] = NULL;
        in[0] = s;
        k10_int("risky.struct.echo.ok", mlxK10echo(1, out, 1, in));
        k10_line("risky.struct.echo", out[0]);
        break;
    }
    default: printf("risky %d: no such case\n", n); break;
    }
    printf("risky %d: returned\n", n);
    fflush(stdout);
}

static int Run(int argc, const char **argv)
{
    bool ok = k10libInitializeWithHandlers(k10_err_handler, k10_out_handler);
    k10_int("initialize", ok);
    if (!ok)
        return 1;
    k10_int("initialize.again", k10libInitializeWithHandlers(k10_err_handler, k10_out_handler));
    k10_int("quiet.ok", mlxK10quiet(0, NULL, 0, NULL));
    printf("model: %s\n", K10_MODEL);
    fflush(stdout);
    if (argc > 2 && strcmp(argv[1], "risky") == 0)
    {
        Risky(atoi(argv[2]));
    }
    else
    {
        RoundTrips();
        Makes();
        Objects();
        Outputs();
        Errors();
        Printing();
    }

    k10libTerminate();
    printf("done\n");
    fflush(stdout);
    return 0;
}

int main(int argc, const char **argv)
{
    if (!mclInitializeApplication(NULL, 0))
    {
        fprintf(stderr, "mclInitializeApplication failed\n");
        return 1;
    }

    {
        int code = mclRunMain((mclMainFcnType)Run, argc, argv);
        mclTerminateApplication();
        return code;
    }
}
