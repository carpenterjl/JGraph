/*
 * jgrtclient: a C program of the JGraph Runtime's native bridge for NativeBridgeTests (compiler and
 * SDK plan, stage K10), built by native\jgrt\build.ps1 against native\jgrt\include and jgrt.lib and
 * checked in prebuilt beside this file, so the test lanes need no C compiler.
 *
 *   jgrtclient <archive.ctf>
 *
 * Opens the archive with mclInitializeComponentInstanceCtfFileToCache (no generated library) and
 * calls the functions of tools\matlab-checklist\compiler-probes\src\k10 through mclFeval and
 * mclMlfFeval, printing one line per question in the probe's form (k10common.h), so its lines can
 * be held against R2025b's in recordings\k10_feval_separate.out.txt.
 */
#include "k10common.h"

static HMCRINSTANCE Instance = NULL;

static mxArray *Call1(const char *name, mxArray *x, bool *ok)
{
    mxArray *out[1] = { NULL };
    mxArray *in[1];
    in[0] = x;
    *ok = mclFeval(Instance, name, 1, out, x == NULL ? 0 : 1, in);
    return out[0];
}

static void Info(const char *label, mxArray *x)
{
    bool ok;
    mxArray *s = Call1("k10info", x, &ok);
    char *text = ok && s != NULL ? mxArrayToString(s) : NULL;
    k10_say(label, ok ? text : "(call failed)");
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
        const char *file = strrchr(stack[i], '\\');
        sprintf(key, "%s.frame%d", label, i);
        /* The frame's file in the runtime cache differs by run: from its file name on. */
        k10_say(key, file == NULL ? stack[i] : file + 1);
    }
    if (depth > 0)
        mclFreeStackTrace(&stack, depth);
    sprintf(key, "%s.lastmessage", label);
    k10_say(key, mclGetLastErrorMessage());
}

int main(int argc, char **argv)
{
    bool ok;
    mxArray *x, *y, *kind;
    if (argc < 2)
    {
        printf("usage: jgrtclient <archive.ctf>\n");
        return 2;
    }

    k10_int("proxy", mclmcrInitialize());
    k10_int("initialize", mclInitializeApplication(NULL, 0));
    k10_int("initialize.again", mclInitializeApplication(NULL, 0));
    k10_int("open", mclInitializeComponentInstanceCtfFileToCache(&Instance, k10_err_handler, k10_out_handler, argv[1]));
    if (Instance == NULL)
        return 1;

    /* Values in and back out (k10_feval in.*, echo.*, info.*). */
    {
        static const double six[] = { 1, 2, 3, 4, 5, 6 };
        x = mxCreateDoubleMatrix(2, 3, mxREAL);
        memcpy(mxGetData(x), six, sizeof(six));
        y = Call1("k10echo", x, &ok);
        k10_line("echo.matrix", y);
        Info("info.matrix", x);
        mxDestroyArray(y);
        mxDestroyArray(x);
    }
    x = mxCreateCellMatrix(1, 3);
    mxSetCell(x, 0, mxCreateDoubleScalar(1));
    mxSetCell(x, 1, mxCreateString("two"));
    Info("info.cell.null", x);
    mxDestroyArray(x);
    x = mxCreateDoubleMatrix(1, 2, mxCOMPLEX);
    {
#if MX_HAS_INTERLEAVED_COMPLEX
        mxComplexDouble *z = mxGetComplexDoubles(x);
        z[0].real = 1; z[0].imag = 2; z[1].real = 3; z[1].imag = -4;
#else
        double *re = mxGetPr(x), *im = mxGetPi(x);
        re[0] = 1; im[0] = 2; re[1] = 3; im[1] = -4;
#endif
    }
    y = Call1("k10echo", x, &ok);
    k10_line("echo.complex", y);
    mxDestroyArray(y);
    mxDestroyArray(x);

    /* A value C cannot hold comes back as a reference, and goes back in (make.string, make.object). */
    kind = mxCreateString("string");
    y = Call1("k10make", kind, &ok);
    k10_line("make.string", y);
    Info("make.string.info", y);
    mxDestroyArray(y);
    mxDestroyArray(kind);
    kind = mxCreateString("object");
    y = Call1("k10make", kind, &ok);
    k10_line("object", y);
    k10_int("object.isclass.k10obj", mxIsClass(y, "k10obj"));
    k10_line("object.get.Value", x = mxGetProperty(y, 0, "Value"));
    mxDestroyArray(x);
    k10_line("object.get.Nope", mxGetProperty(y, 0, "Nope"));
    mxDestroyArray(y);
    mxDestroyArray(kind);
    kind = mxCreateString("value");
    y = Call1("k10make", kind, &ok);
    {
        mxArray *dup = mxDuplicateArray(y), *v = mxCreateDoubleScalar(10);
        mxSetProperty(dup, 0, "Value", v);
        mxDestroyArray(v);
        k10_line("value.dup.set.original", v = mxGetProperty(y, 0, "Value"));
        mxDestroyArray(v);
        k10_line("value.dup.set.dup", v = mxGetProperty(dup, 0, "Value"));
        mxDestroyArray(v);
        mxDestroyArray(dup);
    }
    mxDestroyArray(y);
    mxDestroyArray(kind);

    /* Outputs asked for, errors and the stack (two.nlhs*, addtwo.*, err.*). */
    {
        mxArray *out[3] = { NULL, NULL, NULL }, *in[1];
        in[0] = mxCreateDoubleScalar(3);
        k10_int("two.nlhs3.ok", mclFeval(Instance, "k10two", 3, out, 1, in));
        k10_int("two.nlhs0.ok", mclFeval(Instance, "k10two", 0, out, 1, in));
        k10_line("two.nlhs0.out0", out[0]);
        mxDestroyArray(in[0]);
    }
    kind = mxCreateString("nested");
    printf("err.nested: calling\n");
    fflush(stdout);
    k10_int("err.nested.ok", mclFeval(Instance, "k10err", 0, NULL, 1, &kind));
    Stack("err.nested");
    mxDestroyArray(kind);

    /* What a call prints, piece by piece (print). */
    x = mxCreateDoubleScalar(4);
    k10_int("print.ok", mclFeval(Instance, "k10print", 0, NULL, 1, &x));
    mxDestroyArray(x);

    /* mclMlfFeval with varargin and varargout (mlf.mixed.3). */
    {
        mxArray *a = NULL, *vout = NULL, *vin = mxCreateCellMatrix(1, 3);
        x = mxCreateDoubleScalar(3);
        mxSetCell(vin, 0, mxDuplicateArray(x));
        mxSetCell(vin, 1, mxCreateString("s"));
        mxSetCell(vin, 2, mxCreateDoubleScalar(7));
        k10_int("mlf.mixed.3.ok", mclMlfFeval(Instance, "k10mixed", 3, -2, -2, &a, &vout, x, vin));
        k10_line("mlf.mixed.3.a", a);
        k10_line("mlf.mixed.3.vout", vout);
        mxDestroyArray(a);
        mxDestroyArray(vout);
        mxDestroyArray(vin);
        mxDestroyArray(x);
    }

    /* What R2025b's runtime crashes on is refused (risky.nullinput). */
    {
        mxArray *out[1] = { NULL }, *in[1] = { NULL };
        k10_int("risky.nullinput.ok", mclFeval(Instance, "k10info", 1, out, 1, in));
    }

    k10_int("terminate.instance", mclTerminateInstance(&Instance));
    k10_int("terminate", mclTerminateApplication());
    k10_int("initialized.after", mclIsMCRInitialized());
    printf("done\n");
    return 0;
}
