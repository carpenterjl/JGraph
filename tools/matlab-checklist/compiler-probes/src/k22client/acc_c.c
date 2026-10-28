/*
 * acc_c: the acceptance set's C client (compiler and SDK plan, stage K22) of the C shared library
 * acclib built from src\k22, compiled against the library's own generated acclib.h and acclib.lib:
 * R2025b's build against R2025b's extern\include and mclmcrrt.lib, JGraph's against the JGraph
 * Runtime's include and jgrt.lib. One label<TAB>result line per question, so the two transcripts
 * compare line for line.
 */
#include "acclib.h"
#include <stdio.h>
#include <string.h>

static void Number(const char *label, const mxArray *a)
{
    size_t n, k;
    if (a == NULL)
    {
        printf("%s\tnull\n", label);
        return;
    }
    n = mxGetNumberOfElements(a);
    printf("%s\t%s %dx%d", label, mxGetClassName(a), (int)mxGetM(a), (int)mxGetN(a));
    for (k = 0; k < n && k < 8; k++)
        printf(" %g", mxGetPr(a)[k]);
    printf("\n");
}

static void Text(const char *label, const mxArray *a)
{
    char *s;
    if (a == NULL)
    {
        printf("%s\tnull\n", label);
        return;
    }
    s = mxArrayToString(a);
    printf("%s\t%s %dx%d <<%s>>\n", label, mxGetClassName(a), (int)mxGetM(a), (int)mxGetN(a), s == NULL ? "" : s);
    mxFree(s);
}

static void Field(const char *label, const mxArray *s, const char *name)
{
    const mxArray *f = s == NULL ? NULL : mxGetField(s, 0, name);
    printf("%s.%s\t%g\n", label, name, f == NULL ? -1.0 : mxGetScalar(f));
}

static void Flag(const char *label, bool ok)
{
    printf("%s\t%s\n", label, ok ? "ok" : "failed");
}

static int Run(int argc, const char **argv)
{
    mxArray *out = NULL, *a, *b, *v, *x, *k, *name, *msg;
    double data[5] = { 3, 1, 4, 1, 5 };
    double matrix[4] = { 1, 3, 2, 4 };
    bool ok;
    (void)argc;
    (void)argv;

    if (!acclibInitialize())
    {
        printf("initialize\tfailed\n");
        return 1;
    }
    printf("initialize\tok\n");
    Flag("quiet", mlfAcc_quiet());

    a = mxCreateDoubleScalar(2);
    b = mxCreateDoubleScalar(3);
    ok = mlfAcc_add(1, &out, a, b);
    Flag("add.ok", ok);
    Number("add", out);
    mxDestroyArray(out);
    out = NULL;

    v = mxCreateDoubleMatrix(1, 5, mxREAL);
    memcpy(mxGetPr(v), data, sizeof data);
    ok = mlfAcc_stats(1, &out, v);
    Flag("stats.ok", ok);
    printf("stats\t%s fields %d\n", out == NULL ? "null" : mxGetClassName(out), out == NULL ? 0 : mxGetNumberOfFields(out));
    Field("stats", out, "n");
    Field("stats", out, "mean");
    Field("stats", out, "max");
    mxDestroyArray(out);
    out = NULL;

    name = mxCreateString("Ada");
    ok = mlfAcc_greet(1, &out, name);
    Flag("greet.ok", ok);
    Text("greet", out);
    mxDestroyArray(out);
    out = NULL;

    x = mxCreateDoubleMatrix(2, 2, mxREAL);
    memcpy(mxGetPr(x), matrix, sizeof matrix);
    k = mxCreateDoubleScalar(10);
    ok = mlfAcc_scale(1, &out, x, k);
    Flag("scale.ok", ok);
    Number("scale", out);
    mxDestroyArray(out);
    out = NULL;

    ok = mlfAcc_count(1, &out);
    Flag("count.1.ok", ok);
    Number("count.1", out);
    mxDestroyArray(out);
    out = NULL;
    ok = mlfAcc_count(1, &out);
    Flag("count.2.ok", ok);
    Number("count.2", out);
    mxDestroyArray(out);
    out = NULL;

    msg = mxCreateString("from C");
    ok = mlfAcc_fail(msg);
    Flag("fail.ok", ok);
    printf("fail.message\t%s\n", mclGetLastErrorMessage());

    ok = mlfAcc_add(1, &out, name, b);
    Flag("add.wrongclass.ok", ok);
    printf("add.wrongclass.message\t%s\n", mclGetLastErrorMessage());
    if (out != NULL)
        mxDestroyArray(out);
    out = NULL;

    ok = mlfAcc_count(1, &out);
    Flag("count.3.ok", ok);
    Number("count.3", out);
    mxDestroyArray(out);

    mxDestroyArray(a);
    mxDestroyArray(b);
    mxDestroyArray(v);
    mxDestroyArray(x);
    mxDestroyArray(k);
    mxDestroyArray(name);
    mxDestroyArray(msg);
    acclibTerminate();
    printf("terminate\tok\n");
    /* Without this R2025b's runtime keeps the process alive after main returns. */
    printf("terminate.application\t%d\n", mclTerminateApplication() ? 1 : 0);
    return 0;
}

int main(int argc, const char **argv)
{
    /* Every line reaches the transcript even if the process is killed. */
    setvbuf(stdout, NULL, _IONBF, 0);
    if (!mclInitializeApplication(NULL, 0))
    {
        fprintf(stderr, "mclInitializeApplication failed\n");
        return 1;
    }
    return mclRunMain((mclMainFcnType)Run, argc, argv);
}
