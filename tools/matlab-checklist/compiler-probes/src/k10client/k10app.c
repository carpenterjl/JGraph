/*
 * k10app: the mcl application API (compiler and SDK plan, stage K10). One case per process:
 *
 *   k10app plain        initialize, again, terminate, again, and what each answers
 *   k10app noproxy      mclInitializeApplication without mclmcrInitialize first
 *   k10app before       mx calls before the application is initialized
 *   k10app options      -nojvm and -singleCompThread
 *   k10app logfile F    -logfile F, then the file's text after termination
 *   k10app startmsg     -startmsg and -completemsg as options
 *   k10app unknown      an option the runtime does not know
 *   k10app runmain ...  mclRunMain with a main that prints its arguments and returns 7
 *   k10app split F      -logfile and its file name as one option, "-logfile,F"
 */
#include "k10common.h"

static int RunMain(int argc, const char **argv)
{
    int i;
    printf("runmain.argc: %d\n", argc);
    for (i = 0; i < argc; i++)
    {
        printf("runmain.argv%d: ", i);
        k10_escaped(i == 0 ? "(program)" : argv[i]);
        putchar('\n');
    }
    k10_int("runmain.initialized", mclIsMCRInitialized());
    fflush(stdout);
    return 7;
}

/* The log file's name as it is, so the recording's tokens replace its folder. */
static void LogFileName(const char *label)
{
    const char *name = mclGetLogFileName();
    printf("%s: %s\n", label, name == NULL ? "(null)" : name);
    fflush(stdout);
}

static void Initialize(const char *label, const char **options, size_t count)
{
    char key[64];
    bool ok = mclInitializeApplication(options, count);
    sprintf(key, "%s.initialize", label);
    k10_int(key, ok);
    sprintf(key, "%s.initialized", label);
    k10_int(key, mclIsMCRInitialized());
    sprintf(key, "%s.logfile", label);
    LogFileName(key);
}

static void PrintFile(const char *label, const char *path)
{
    char text[4096];
    size_t n;
    FILE *f = fopen(path, "rb");
    if (f == NULL)
    {
        k10_say(label, "(no file)");
        return;
    }

    n = fread(text, 1, sizeof(text) - 1, f);
    fclose(f);
    text[n] = 0;
    k10_say(label, text);
}

int main(int argc, char **argv)
{
    const char *mode = argc > 1 ? argv[1] : "plain";
    printf("mode: %s\n", mode);
    fflush(stdout);
    if (strcmp(mode, "noproxy") != 0)
    {
        k10_int("proxy.initialize", mclmcrInitialize());
        k10_int("proxy.initialize.again", mclmcrInitialize());
    }

    if (strcmp(mode, "plain") == 0)
    {
        k10_int("before.initialized", mclIsMCRInitialized());
        LogFileName("before.logfile");
        Initialize("first", NULL, 0);
        Initialize("second", NULL, 0);
        k10_int("terminate", mclTerminateApplication());
        k10_int("after.initialized", mclIsMCRInitialized());
        Initialize("third", NULL, 0);
        k10_int("terminate.again", mclTerminateApplication());
    }
    else if (strcmp(mode, "noproxy") == 0)
    {
        Initialize("noproxy", NULL, 0);
        k10_int("terminate", mclTerminateApplication());
    }
    else if (strcmp(mode, "before") == 0)
    {
        mxArray *a = mxCreateDoubleScalar(3);
        k10_line("before.scalar", a);
        Initialize("init", NULL, 0);
        k10_line("after.scalar", a);
        mxDestroyArray(a);
        k10_int("terminate", mclTerminateApplication());
        a = mxCreateString("after terminate");
        k10_line("terminated.string", a);
        mxDestroyArray(a);
    }
    else if (strcmp(mode, "options") == 0)
    {
        const char *options[] = { "-nojvm", "-singleCompThread" };
        Initialize("options", options, 2);
        k10_int("terminate", mclTerminateApplication());
    }
    else if (strcmp(mode, "logfile") == 0 && argc > 2)
    {
        const char *options[] = { "-logfile", argv[2] };
        Initialize("logfile", options, 2);
        printf("a line the client prints\n");
        fflush(stdout);
        k10_int("terminate", mclTerminateApplication());
        PrintFile("logfile.text", argv[2]);
    }
    else if (strcmp(mode, "split") == 0 && argc > 2)
    {
        char option[512];
        const char *options[1];
        sprintf(option, "-logfile,%s", argv[2]);
        options[0] = option;
        Initialize("split", options, 1);
        k10_int("terminate", mclTerminateApplication());
        PrintFile("split.text", argv[2]);
    }
    else if (strcmp(mode, "startmsg") == 0)
    {
        const char *options[] = { "-startmsg", "-completemsg", "complete" };
        Initialize("startmsg", options, 3);
        k10_int("terminate", mclTerminateApplication());
    }
    else if (strcmp(mode, "unknown") == 0)
    {
        const char *options[] = { "-bogus" };
        Initialize("unknown", options, 1);
        k10_int("terminate", mclTerminateApplication());
    }
    else if (strcmp(mode, "runmain") == 0)
    {
        int code;
        Initialize("runmain", NULL, 0);
        code = mclRunMain((mclMainFcnType)RunMain, argc - 1, (const char **)(argv + 1));
        k10_int("runmain.result", code);
        k10_int("terminate", mclTerminateApplication());
    }
    else
    {
        printf("no such mode\n");
        return 2;
    }

    printf("done\n");
    return 0;
}
