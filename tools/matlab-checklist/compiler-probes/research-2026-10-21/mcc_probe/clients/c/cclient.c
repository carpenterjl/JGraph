/* Minimal client of the mcc -W lib:libprobe C shared library. */
#include <stdio.h>
#include "libprobe.h"

static int run_main(int argc, const char **argv)
{
    mxArray *a, *b, *c = NULL;
    (void)argc; (void)argv;
    if (!libprobeInitialize()) {
        fprintf(stderr, "libprobeInitialize failed\n");
        return 2;
    }
    a = mxCreateDoubleScalar(1.5);
    b = mxCreateDoubleScalar(2.25);
    if (!mlfAddtwo(1, &c, a, b)) {
        fprintf(stderr, "mlfAddtwo failed\n");
        return 3;
    }
    printf("mlfAddtwo -> class=%s value=%g\n", mxGetClassName(c), mxGetScalar(c));
    /* an error path: three inputs */
    {
        mxArray *plhs[1] = {NULL};
        mxArray *prhs[3];
        prhs[0] = a; prhs[1] = b; prhs[2] = b;
        if (!mlxAddtwo(1, plhs, 3, prhs)) {
            printf("mlxAddtwo with 3 inputs returned false; stack:\n");
            libprobePrintStackTrace();
        }
    }
    mxDestroyArray(a); mxDestroyArray(b); mxDestroyArray(c);
    libprobeTerminate();
    return 0;
}

int main(int argc, const char **argv)
{
    if (!mclInitializeApplication(NULL, 0)) {
        fprintf(stderr, "mclInitializeApplication failed\n");
        return 1;
    }
    return mclRunMain((mclMainFcnType)run_main, argc, argv);
}
