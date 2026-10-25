/*
 * k11client: a client of the C shared library k11lib that probe_k11_build.m builds from src\k11
 * (compiler and SDK plan, stage K11), compiled against the library's own generated k11lib.h and
 * k11lib.lib: R2025b's build against R2025b's extern\include and mclmcrrt.lib, JGraph's against the
 * JGraph Runtime's include and jgrt.lib. It calls every generated function in its mlx and mlf forms
 * and prints one line per question (k10common.h's form), so the two transcripts compare line by line.
 *
 *   k11client          the library started with the client's handlers (InitializeWithHandlers)
 *   k11client plain    started with its own (Initialize), so its default handlers write
 *   k11client twice    started, ended and started again
 */
#include "k11lib.h"
#include "k10common.h"

static mxArray *Num(double v) { return mxCreateDoubleScalar(v); }

static void Free(mxArray **a, int n)
{
    int k;
    for (k = 0; k < n; k++)
    {
        if (a[k] != NULL)
            mxDestroyArray(a[k]);
        a[k] = NULL;
    }
}

static void Lines(const char *label, mxArray **a, int n)
{
    int k;
    char name[96];
    for (k = 0; k < n; k++)
    {
        sprintf(name, "%s.out%d", label, k);
        k10_line(name, a[k]);
    }
}

static mxArray *Cell(int n, double first)
{
    mxArray *c = mxCreateCellMatrix(1, n);
    int k;
    for (k = 0; k < n; k++)
        mxSetCell(c, k, Num(first + k));
    return c;
}

static void Fixed(void)
{
    mxArray *x = Num(3), *y = Num(4), *s = mxCreateString("shout"), *out[5] = { NULL, NULL, NULL, NULL, NULL };
    mxArray *in[5];

    k10_int("plain.mlf.ok", mlfK11plain(1, &out[0], x));
    Lines("plain.mlf", out, 1);
    Free(out, 1);
    k10_int("plain.mlf0.ok", mlfK11plain(0, &out[0], x));
    Lines("plain.mlf0", out, 1);
    Free(out, 1);
    in[0] = x;
    k10_int("plain.mlx.ok", mlxK11plain(1, out, 1, in));
    Lines("plain.mlx", out, 1);
    Free(out, 1);

    k10_int("mixed.mlf1.ok", mlfK11Mixed(1, &out[0], &out[1], x, y));
    Lines("mixed.mlf1", out, 2);
    Free(out, 2);
    k10_int("mixed.mlf2.ok", mlfK11Mixed(2, &out[0], &out[1], x, y));
    Lines("mixed.mlf2", out, 2);
    Free(out, 2);
    in[0] = x; in[1] = y;
    k10_int("mixed.mlx.ok", mlxK11Mixed(2, out, 2, in));
    Lines("mixed.mlx", out, 2);
    Free(out, 2);

    k10_int("upper.mlf.ok", mlfK11upper(1, &out[0], s));
    Lines("upper.mlf", out, 1);
    Free(out, 1);
    in[0] = s;
    k10_int("upper.mlx.ok", mlxK11upper(1, out, 1, in));
    Lines("upper.mlx", out, 1);
    Free(out, 1);

    k10_int("under.mlf.ok", mlfK11_under(1, &out[0], x));
    Lines("under.mlf", out, 1);
    Free(out, 1);

    {
        mxArray *a = Num(1), *b = Num(2), *c = Num(3), *d = Num(4), *e = Num(5);
        k10_int("long.mlf.ok", mlfK11long(3, &out[0], &out[1], &out[2], a, b, c, d, e));
        Lines("long.mlf", out, 3);
        Free(out, 3);
        k10_int("long.mlf2.ok", mlfK11long(2, &out[0], &out[1], &out[2], a, b, c, d, e));
        Lines("long.mlf2", out, 3);
        Free(out, 3);
        in[0] = a; in[1] = b; in[2] = c; in[3] = d; in[4] = e;
        k10_int("long.mlx.ok", mlxK11long(3, out, 5, in));
        Lines("long.mlx", out, 3);
        Free(out, 3);
        k10_int("long.mlx4.ok", mlxK11long(1, out, 4, in));
        Lines("long.mlx4", out, 1);
        Free(out, 1);
        mxDestroyArray(a); mxDestroyArray(b); mxDestroyArray(c); mxDestroyArray(d); mxDestroyArray(e);
    }

    k10_int("wrap.mlf.ok", mlfK11_a_function_name_long_enough_to_make_generated_lines_wrap(1, &out[0], s));
    Lines("wrap.mlf", out, 1);
    Free(out, 1);
    in[0] = x;
    k10_int("wrap.mlx.ok", mlxK11_a_function_name_long_enough_to_make_generated_lines_wrap(1, out, 1, in));
    Lines("wrap.mlx", out, 1);
    Free(out, 1);

    k10_int("sub.mlf.ok", mlfK11sub(1, &out[0], x));
    Lines("sub.mlf", out, 1);
    Free(out, 1);

    k10_int("noparen.mlf.ok", mlfK11noparen());
    k10_int("noparen.mlx.ok", mlxK11noparen(0, out, 0, in));
    k10_int("noparen.mlx1.ok", mlxK11noparen(1, out, 0, in));
    Lines("noparen.mlx1", out, 1);
    Free(out, 1);

    mxDestroyArray(x);
    mxDestroyArray(y);
    mxDestroyArray(s);
}

static void Variable(void)
{
    mxArray *i[8], *vin = Cell(2, 70), *none = mxCreateCellMatrix(0, 0), *out[6] = { NULL, NULL, NULL, NULL, NULL, NULL };
    mxArray *vout = NULL;
    mxArray *in[10];
    int k;
    for (k = 0; k < 8; k++)
        i[k] = Num(k + 1);

    k10_int("wide.mlf6.ok", mlfK11wide(6, &out[0], &out[1], &out[2], &out[3], &vout, i[0], i[1], i[2], i[3], i[4], i[5], i[6], i[7], vin));
    Lines("wide.mlf6", out, 4);
    k10_line("wide.mlf6.vout", vout);
    Free(out, 4);
    Free(&vout, 1);
    k10_int("wide.mlf4.ok", mlfK11wide(4, &out[0], &out[1], &out[2], &out[3], &vout, i[0], i[1], i[2], i[3], i[4], i[5], i[6], i[7], none));
    Lines("wide.mlf4", out, 4);
    k10_line("wide.mlf4.vout", vout);
    Free(out, 4);
    Free(&vout, 1);
    for (k = 0; k < 8; k++)
        in[k] = i[k];
    in[8] = vin;
    k10_int("wide.mlx.ok", mlxK11wide(6, out, 9, in));
    Lines("wide.mlx", out, 6);
    Free(out, 6);

    k10_int("outonly.mlf3.ok", mlfK11outonly(3, &out[0], &vout));
    Lines("outonly.mlf3", out, 1);
    k10_line("outonly.mlf3.vout", vout);
    Free(out, 1);
    Free(&vout, 1);
    k10_int("outonly.mlf1.ok", mlfK11outonly(1, &out[0], &vout));
    Lines("outonly.mlf1", out, 1);
    k10_line("outonly.mlf1.vout", vout);
    Free(out, 1);
    Free(&vout, 1);
    k10_int("outonly.mlx.ok", mlxK11outonly(3, out, 0, in));
    Lines("outonly.mlx", out, 3);
    Free(out, 3);

    k10_int("varin.mlf.ok", mlfK11varin(vin));
    k10_int("varin.mlf.none.ok", mlfK11varin(none));
    k10_int("varin.mlx.ok", mlxK11varin(0, out, 3, i));

    for (k = 0; k < 8; k++)
        mxDestroyArray(i[k]);
    mxDestroyArray(vin);
    mxDestroyArray(none);
}

static void Errors(void)
{
    mxArray *m = mxCreateString("the client asked for this"), *x = Num(1), *out[2] = { NULL, NULL };
    mxArray *in[3];
    printf("fail.mlf: calling\n");
    fflush(stdout);
    k10_int("fail.mlf.ok", mlfK11fail(m));
    printf("fail.stack: printing\n");
    fflush(stdout);
    k11libPrintStackTrace();
    printf("fail.stack: printed\n");
    fflush(stdout);
    in[0] = x; in[1] = x; in[2] = x;
    k10_int("plain.toomany.ok", mlxK11plain(1, out, 3, in));
    k10_int("plain.toomanyout.ok", mlxK11plain(2, out, 1, in));
    Lines("plain.toomanyout", out, 2);
    Free(out, 2);
    k10_int("fail.mlx.none.ok", mlxK11fail(0, out, 0, in));
    mxDestroyArray(m);
    mxDestroyArray(x);
}

static int Run(int argc, const char **argv)
{
    const char *mode = argc > 1 ? argv[1] : "handlers";
    bool started;
    if (strcmp(mode, "plain") == 0)
        started = k11libInitialize();
    else
        started = k11libInitializeWithHandlers(k10_err_handler, k10_out_handler);
    k10_int("initialize", started);
    if (!started)
        return 2;
    k10_int("initialize.again", k11libInitializeWithHandlers(k10_err_handler, k10_out_handler));
    k10_int("quiet.ok", mlfK11quiet());
    if (strcmp(mode, "twice") == 0)
    {
        mxArray *x = Num(5), *y = NULL;
        k10_int("twice.first.ok", mlfK11plain(1, &y, x));
        k10_line("twice.first.y", y);
        Free(&y, 1);
        k11libTerminate();
        printf("twice: terminated\n");
        fflush(stdout);
        k10_int("twice.after.ok", mlfK11plain(1, &y, x));
        k10_line("twice.after.y", y);
        Free(&y, 1);
        k10_int("twice.restart", k11libInitialize());
        k10_int("twice.second.ok", mlfK11plain(1, &y, x));
        k10_line("twice.second.y", y);
        Free(&y, 1);
        mxDestroyArray(x);
    }
    else
    {
        Fixed();
        Variable();
        Errors();
    }

    k11libTerminate();
    k11libTerminate();
    /* Without this R2025b's runtime keeps the process from ending once main returns. */
    k10_int("terminate.application", mclTerminateApplication());
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

    return mclRunMain((mclMainFcnType)Run, argc, argv);
}
